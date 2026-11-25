using System.Text.Json;
using System.Text.RegularExpressions;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Core.Storage;

namespace Orchestrator.Core.Sessions;

/// <summary>A registry call that breaks a rule of the registry. Nothing changed.</summary>
public sealed class RegistryException(string message) : Exception(message);

/// <summary>The spec of a new session. Each field that is null takes the rule of <see cref="SessionRegistry.Create"/>.</summary>
public sealed record SessionSpec
{
    /// <summary>The registry id. Null: a new id.</summary>
    public string? Id { get; init; }

    /// <summary>The own code. Null: the next free code (<see cref="SessionRegistry.NextCode"/>).</summary>
    public string? Code { get; init; }

    public string? Parent { get; init; }
    public string? Title { get; init; }
    public string? Owner { get; init; }
    public List<string>? Tags { get; init; }

    /// <summary>The stream. Null: the stream of the parent.</summary>
    public string? Stream { get; init; }

    /// <summary>The role. Null: <see cref="Roles.Initial"/>.</summary>
    public string? Role { get; init; }

    public SessionType? Type { get; init; }
    public string? Workflow { get; init; }
    public SessionWork Work { get; init; } = new();

    /// <summary>The values that the creator sets. Each other value is a copy of the parent value (a root: the defaults).</summary>
    public SessionSettings? Settings { get; init; }

    /// <summary>The engine references. <see cref="EngineRef.SessionId"/> null: the registry id.</summary>
    public EngineRef Engine { get; init; } = new();

    /// <summary>True: the engine session exists already (a session that joins with orch enable).</summary>
    public bool? Started { get; init; }

    /// <summary>The party that creates the session.</summary>
    public required string By { get; init; }
}

/// <summary>
/// The slice of the session registry (S1, S2): the only writer of <see cref="SessionEntry"/>. It runs on the generic
/// repository of H2, so the place of the entries (files, a database, an API) is a choice of the infrastructure.
/// </summary>
public sealed partial class SessionRegistry(IRepository<Doc<SessionEntry>, string> entries, TimeProvider? clock = null, Func<string, bool>? modeExists = null)
{
    const int Attempts = 20;
    public const string RootPrefix = "O";
    public const string ChildPrefix = "C";

    readonly TimeProvider clock = clock ?? TimeProvider.System;

    string Now => Times.Format(clock.GetUtcNow());

    public SessionEntry? Get(string id) => entries.Read(id);

    /// <summary>The entry, or an empty entry with this id when the registry has none.</summary>
    public SessionEntry Of(string id) => Get(id) ?? new SessionEntry(id);

    public bool Exists(string id) => entries.Exists(id);

    public IReadOnlyList<SessionEntry> List(Func<SessionEntry, bool>? where = null) =>
        entries.List(_ => true).Select(d => d.Value).Where(e => e is not null && (where is null || where(e))).ToList();

    /// <summary>The children of a session, oldest first. The registry keeps only the parent, so the two sides cannot disagree.</summary>
    public IReadOnlyList<SessionEntry> Children(string id) =>
        List(e => e.Parent == id).OrderBy(e => e.Created is null).ThenBy(e => e.Created?.At, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Adds a new entry. The code is the code of the spec or the next free code. Each setting that the spec does not set
    /// is a copy of the value of the parent now (a root: the defaults of <see cref="SettingTiers"/>).
    /// </summary>
    public SessionEntry Create(SessionSpec spec)
    {
        var id = string.IsNullOrEmpty(spec.Id) ? Guid.NewGuid().ToString() : spec.Id;
        var parent = spec.Parent is { } p ? Get(p) : null;
        foreach (var (tier, key, value) in spec.Settings?.Entries() ?? [])
            Throw(SettingTiers.Check(tier, key, value, modeExists));
        var code = spec.Code is { } c ? CheckCode(c, spec.Parent, id) : NextCode(spec.Parent);
        if (spec.Role is { } role && !Roles.All.Contains(role)) throw new RegistryException($"role {role} is unknown (known: {string.Join(", ", Roles.All)})");
        var stamp = new Stamp(Now, spec.By);
        var entry = new SessionEntry(id)
        {
            Code = code,
            Engine = spec.Engine with { SessionId = spec.Engine.SessionId ?? id },
            Parent = spec.Parent,
            Title = spec.Title,
            Owner = spec.Owner ?? parent?.Owner,
            Tags = spec.Tags,
            Stream = spec.Stream ?? parent?.Stream,
            Role = spec.Role ?? Roles.Initial(spec.Type?.Agent, spec.Parent),
            Type = spec.Type,
            Workflow = spec.Workflow,
            Work = spec.Work,
            Settings = SessionSettings.Merge(spec.Settings, spec.Parent is { } pid ? Resolve(pid) : SettingTiers.Defaults()),
            Lifecycle = new SessionLifecycle { Phase = ControlStates.Active, At = stamp.At, By = spec.By, Started = spec.Started },
            Created = stamp,
            Updated = stamp,
        };
        if (!entries.TryCreate(id, entry)) throw new RegistryException($"session {id} exists in the registry. Nothing changed.");
        return entry;
    }

    /// <summary>
    /// Changes one entry: a read, the change and a save with the version of the read. When another writer saved first,
    /// it reads again and changes again. The change cannot change the id.
    /// </summary>
    public SessionEntry Update(string id, Func<SessionEntry, SessionEntry> change, string by)
    {
        for (var attempt = 1; ; attempt++)
        {
            var doc = entries.Get(id) ?? throw new RegistryException($"session {id} is not in the registry");
            var changed = change(doc.Value);
            if (changed.Id != id) throw new RegistryException($"an update cannot change the id of {id}");
            if (changed == doc.Value) return doc.Value;
            doc.Value = changed with { Updated = new Stamp(Now, by) };
            try
            {
                entries.Save();
                return doc.Value;
            }
            catch (StoreConflictException e) when (e.Conflict == StoreConflict.Stale && attempt < Attempts)
            {
            }
        }
    }

    /// <summary>Sets the phase: active, stopped or archived.</summary>
    public SessionEntry SetPhase(string id, string phase, string by, string? reason = null)
    {
        if (phase is not (ControlStates.Active or ControlStates.Stopped or ControlStates.Archived)) throw new RegistryException($"phase {phase} is unknown");
        if (!Exists(id)) Create(new SessionSpec { Id = id, By = by });
        return Update(id, e => e with { Lifecycle = e.Lifecycle with { Phase = phase, At = Now, By = by, Reason = reason } }, by);
    }

    /// <summary>
    /// Sets one setting of a session. Only the user and the parent of the session may change a setting. The change does
    /// not go to the children: they keep the copy that they got at create.
    /// </summary>
    public SessionEntry Configure(string id, string tier, string key, JsonElement value, string by)
    {
        var entry = Get(id) ?? throw new RegistryException($"session {id} is not in the registry");
        if (by != Parties.User && (entry.Parent is null || by != Parties.Session(entry.Parent)))
            throw new RegistryException($"{by} may not change the settings of {id}: only the user and the parent may");
        Throw(SettingTiers.Check(tier, key, value, modeExists));
        return Update(id, e => e with { Settings = e.Settings.With(tier, key, value) }, by);
    }

    /// <summary>
    /// The settings in use: the own value first, then the value of the parent (also resolved), then the defaults. An entry
    /// from create has each value; the chain fills a key that a later version of the tiers added.
    /// </summary>
    public SessionSettings Resolve(string id)
    {
        var chain = Chain(id);
        var settings = SettingTiers.Defaults();
        foreach (var entry in chain) settings = SessionSettings.Merge(entry.Settings, settings);
        return settings;
    }

    /// <summary>The codes from the root down, for example <c>O2 C1 C3</c>. A session with no code shows its short id.</summary>
    public string FullCode(string id) => string.Join(" ", Chain(id).Select(e => e.Code ?? Short(e.Id)));

    /// <summary>The title in the app: <c>[&lt;full code&gt;] &lt;title&gt;</c>.</summary>
    public string AppTitle(string id) => $"[{FullCode(id)}] {Of(id).Title ?? Short(id)}";

    /// <summary>The next free code under a parent: <c>O&lt;n&gt;</c> for a root, <c>C&lt;n&gt;</c> for a child.</summary>
    public string NextCode(string? parent) => NextCode(parent, Siblings(parent, null));

    /// <summary>Gives the next free code to each entry with no code, oldest first. The count of changed entries.</summary>
    public int AssignCodes(string by)
    {
        var count = 0;
        foreach (var entry in List(e => e.Code is null).OrderBy(e => e.Created is null).ThenBy(e => e.Created?.At, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal))
        {
            Update(entry.Id, e => e.Code is null ? e with { Code = NextCode(e.Parent, Siblings(e.Parent, e.Id)) } : e, by);
            count++;
        }
        return count;
    }

    /// <summary>The entries from the root down to the session. A loop in the parent links stops the chain.</summary>
    public IReadOnlyList<SessionEntry> Chain(string id)
    {
        var chain = new List<SessionEntry>();
        var seen = new HashSet<string>();
        for (var current = (string?)id; current is not null && seen.Add(current); current = chain[^1].Parent)
            chain.Add(Of(current));
        chain.Reverse();
        return chain;
    }

    IReadOnlyList<SessionEntry> Siblings(string? parent, string? except) => List(e => e.Parent == parent && e.Id != except);

    static string NextCode(string? parent, IReadOnlyList<SessionEntry> siblings)
    {
        var prefix = parent is null ? RootPrefix : ChildPrefix;
        var used = siblings.Select(e => e.Code).OfType<string>()
            .Select(code => code.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(code[prefix.Length..], out var n) ? n : 0);
        return $"{prefix}{used.DefaultIfEmpty(0).Max() + 1}";
    }

    string CheckCode(string code, string? parent, string id)
    {
        if (!CodePattern().IsMatch(code)) throw new RegistryException($"code {code} is not valid: a letter, then up to 15 letters, digits or dashes");
        if (Siblings(parent, id).Any(e => e.Code == code)) throw new RegistryException($"code {code} is used by a sibling. Nothing changed.");
        return code;
    }

    static void Throw(IReadOnlyList<string> errors)
    {
        if (errors.Count > 0) throw new RegistryException(string.Join("; ", errors));
    }

    static string Short(string id) => id.Length > 8 ? id[..8] : id;

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]{0,15}$")]
    private static partial Regex CodePattern();
}
