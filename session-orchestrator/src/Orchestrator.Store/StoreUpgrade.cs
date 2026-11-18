using Orchestrator.Core.Contract;
using Orchestrator.Core.Leases;
using Orchestrator.Core.Sessions;
using Orchestrator.Core.Storage;

namespace Orchestrator.Store;

/// <summary>The result of an upgrade: the version before it, the new entries, and one line for each session.</summary>
public sealed record UpgradeResult(int From, int Entries, IReadOnlyList<string> Lines);

/// <summary>
/// The upgrade of a store from version 2 to version 3 (S2): <c>meta.json</c> and <c>control.json</c> of each session
/// become one registry entry, <c>session.json</c>. It runs only through the repositories. It keeps the old files as a
/// backup and writes <c>version.json</c> last, so a stopped upgrade can run again.
/// </summary>
public static class StoreUpgrade
{
    /// <summary>A session with a heartbeat in this window still writes to the store.</summary>
    public static readonly TimeSpan LiveWindow = TimeSpan.FromSeconds(30);

    public const string By = "store-upgrade";

    /// <summary>The sessions with a heartbeat in the last <see cref="LiveWindow"/>.</summary>
    public static IReadOnlyList<string> LiveSessions(IRepositories repos, DateTimeOffset now)
    {
        var beats = repos.SessionFile<string>(StoreFiles.Heartbeat);
        return repos.Sessions.List(_ => true).Select(d => d.Id)
            .Where(id => Times.Parse(beats.Read(id)) is { } beat && now - beat < LiveWindow).ToList();
    }

    /// <summary>
    /// Makes the registry entry of each session from its version 2 files, then sets the store version to 3. A store of
    /// version 3 does not change. A dry run computes the entries and writes nothing.
    /// </summary>
    public static UpgradeResult ToVersion3(IRepositories repos, DateTimeOffset now, string engineKind, bool dryRun = false)
    {
        var version = repos.StoreVersions.Read(StoreIds.Version)?.Version
            ?? throw new InvalidDataException($"store {repos.Location} has no version.json: it is not a store");
        if (version == 3) return new UpgradeResult(3, 0, ["The store has version 3 already. Nothing changed."]);
        if (version != 2) throw new InvalidDataException($"store {repos.Location} has version {version}. The upgrade reads only version 2.");

        var metas = repos.SessionFile<V2Meta>(StoreFiles.Meta);
        var controls = repos.SessionFile<V2Control>(StoreFiles.Control);
        var at = Times.Format(now);
        var old = new Dictionary<string, (V2Meta? Meta, V2Control? Control)>();
        foreach (var id in repos.Sessions.List(_ => true).Select(d => d.Id))
        {
            var meta = metas.Read(id);
            var control = controls.Read(id);
            if (meta is not null || control is not null) old[id] = (meta, control);
        }

        var made = new Dictionary<string, SessionEntry>();
        var lines = new List<string>();
        var codes = Codes(old.ToDictionary(o => o.Key, o => (o.Value.Meta?.Parent, o.Value.Meta?.CreatedAt ?? o.Value.Control?.At)));
        // Parents first, so that each child copies the settings of its parent, as a create does.
        foreach (var id in Order(old.ToDictionary(o => o.Key, o => o.Value.Meta?.Parent)))
        {
            if (repos.SessionEntries.Exists(id))
            {
                lines.Add($"{id}: the entry exists, it stays");
                continue;
            }
            var (meta, control) = old[id];
            var parent = meta?.Parent;
            var fallback = parent is not null && made.TryGetValue(parent, out var p) ? p.Settings
                : parent is not null && repos.SessionEntries.Read(parent) is { } stored ? stored.Settings
                : SettingTiers.Defaults();
            var entry = Entry(id, meta, control, codes[id], SessionSettings.Merge(Own(meta), fallback), engineKind, at);
            made[id] = entry;
            lines.Add($"{id}: {entry.Code} {entry.Title ?? "(no title)"}, phase {entry.Lifecycle.Phase}");
        }

        if (dryRun) return new UpgradeResult(2, made.Count, lines);
        foreach (var entry in made.Values) repos.SessionEntries.TryCreate(entry.Id, entry);
        repos.StoreVersions.Put(StoreIds.Version, new StoreVersion(3, at));
        return new UpgradeResult(2, made.Count, lines);
    }

    static SessionEntry Entry(string id, V2Meta? meta, V2Control? control, string code, SessionSettings settings, string engineKind, string at) => new(id)
    {
        Code = code,
        Engine = new EngineRef { Kind = engineKind, SessionId = id, AppId = meta?.AppId, Imported = meta?.Imported, RemoteControl = meta?.RemoteControl },
        Parent = meta?.Parent,
        Title = meta?.Title,
        Role = Roles.Initial(meta?.Agent, meta?.Parent),
        Type = meta?.Agent is null && meta?.Skills is null ? null : new SessionType { Agent = meta.Agent, Skills = meta.Skills },
        Work = new SessionWork { Cwd = meta?.Cwd, Repo = meta?.Repo, Branch = meta?.Branch, Worktree = meta?.Worktree },
        Settings = settings,
        Lifecycle = new SessionLifecycle
        {
            Phase = control?.State ?? ControlStates.Active,
            At = control?.At,
            By = control?.By,
            Reason = control?.Reason,
            // Version 2 made a new engine session only for a meta with createdBy: with no createdBy, the session exists.
            Started = meta?.Started ?? (meta?.CreatedBy is null ? true : null),
        },
        Created = new Stamp(meta?.CreatedAt ?? control?.At ?? at, meta?.CreatedBy ?? "unknown"),
        Updated = new Stamp(at, By),
    };

    /// <summary>The settings that a version 2 meta set: <c>allowDefaults</c> and <c>background</c>.</summary>
    static SessionSettings Own(V2Meta? meta)
    {
        var settings = new SessionSettings();
        if (meta?.AllowDefaults is { } allow) settings = settings.With(SettingTiers.Permissions, SettingTiers.AllowDefaults, allow);
        if (meta?.Background is { } background) settings = settings.With(SettingTiers.Behavior, SettingTiers.Visibility, background ? SettingTiers.Private : SettingTiers.Public);
        return settings;
    }

    /// <summary>The codes of the registry rule: <c>O&lt;n&gt;</c> for each root and <c>C&lt;n&gt;</c> under each parent, oldest first. An entry with no create time comes last.</summary>
    public static Dictionary<string, string> Codes(IReadOnlyDictionary<string, (string? Parent, string? Created)> sessions)
    {
        var codes = new Dictionary<string, string>();
        foreach (var group in sessions.GroupBy(s => s.Value.Parent))
        {
            var prefix = group.Key is null ? SessionRegistry.RootPrefix : SessionRegistry.ChildPrefix;
            var n = 0;
            foreach (var s in group.OrderBy(s => s.Value.Created is null).ThenBy(s => s.Value.Created, StringComparer.Ordinal).ThenBy(s => s.Key, StringComparer.Ordinal))
                codes[s.Key] = $"{prefix}{++n}";
        }
        return codes;
    }

    /// <summary>Each id after its parent. A parent that is not in the set, or a loop, does not stop the order.</summary>
    static IEnumerable<string> Order(IReadOnlyDictionary<string, string?> parents)
    {
        var done = new HashSet<string>();
        IEnumerable<string> Visit(string id, HashSet<string> path)
        {
            if (done.Contains(id) || !path.Add(id)) yield break;
            if (parents[id] is { } parent && parents.ContainsKey(parent))
                foreach (var up in Visit(parent, path)) yield return up;
            if (done.Add(id)) yield return id;
        }
        return parents.Keys.Order(StringComparer.Ordinal).SelectMany(id => Visit(id, [])).ToList();
    }

    /// <summary>The meta.json of store version 2.</summary>
    internal sealed record V2Meta
    {
        public string? Parent { get; init; }
        public string? Title { get; init; }
        public string? Cwd { get; init; }
        public string? CreatedBy { get; init; }
        public string? CreatedAt { get; init; }
        public bool? Started { get; init; }
        public bool? Worktree { get; init; }
        public string? Agent { get; init; }
        public List<string>? Skills { get; init; }
        public string? Repo { get; init; }
        public string? Branch { get; init; }
        public bool? AllowDefaults { get; init; }
        public bool? Imported { get; init; }
        public string? AppId { get; init; }
        public bool? Background { get; init; }
        public bool? RemoteControl { get; init; }
    }

    /// <summary>The control.json of store version 2.</summary>
    internal sealed record V2Control
    {
        public string? State { get; init; }
        public string? At { get; init; }
        public string? By { get; init; }
        public string? Reason { get; init; }
    }
}
