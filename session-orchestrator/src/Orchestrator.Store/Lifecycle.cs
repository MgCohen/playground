using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Stack;
using Orchestrator.Providers;

namespace Orchestrator.Store;

public sealed class Lifecycle(FileStore store, IProvider? provider = null)
{
    readonly IProvider provider = provider ?? ProviderFactory.Default;

    public const string WorktreeSuffix = ".wt";
    public const string BranchPrefix = "orch/session-";

    public Control Control(string id) => store.ReadJson<Control>(id, StoreFiles.Control) ?? new Control(ControlStates.Active, "", "");

    public bool IsStopped(string id) => Control(id).State is ControlStates.Stopped or ControlStates.Archived;

    public void Stop(string id, string by, string? reason = null)
    {
        store.Events.Append(EventKinds.SessionStop, by, Parties.Session(id), Messages.Fields(("text", reason ?? "stopped"), ("reason", reason)), null, id);
        store.WriteJson(id, new Control(ControlStates.Stopped, store.Now, by) { Reason = reason }, StoreFiles.Control);
    }

    public void Resume(string id, string by)
    {
        store.Events.Append(EventKinds.SessionResume, by, Parties.Session(id), Messages.Fields(("text", "resumed")), null, id);
        store.WriteJson(id, new Control(ControlStates.Active, store.Now, by), StoreFiles.Control);
    }

    public IReadOnlyList<string> ArchiveBlockers(string id)
    {
        var blockers = new List<string>();
        var open = store.OpenDecisions(id);
        if (open.Count > 0) blockers.Add($"{open.Count} open decision(s) wait for the user");
        var prs = store.Stacks(id).SelectMany(e => e.Versions).Where(v => v.State is VersionStates.Open or VersionStates.Ready && v.Pr is not null).Select(v => $"#{v.Pr}").ToList();
        if (prs.Count > 0) blockers.Add($"open PRs: {string.Join(", ", prs)}");
        return blockers;
    }

    public IReadOnlyList<string> Archive(string id, string by, bool force)
    {
        var blockers = ArchiveBlockers(id);
        if (blockers.Count > 0 && !force) throw new MessageException($"session {id} is not archived: {string.Join("; ", blockers)}. Use --force to archive it anyway.");
        var lines = new List<string>();
        store.Events.Append(EventKinds.SessionArchive, by, Parties.Session(id), Messages.Fields(("text", "archived"), ("blockers", blockers)), null, id);
        store.WriteJson(id, new Control(ControlStates.Archived, store.Now, by) { Reason = blockers.Count > 0 ? string.Join("; ", blockers) : null }, StoreFiles.Control);
        store.SetEnabled(id, false);
        lines.Add($"Archived {id}. The supervisor does not run it again.");
        if (store.Meta(id) is { Repo: { } repo, Cwd: { } worktree, Branch: { } branch })
            lines.Add(RemoveWorktree(repo, worktree, branch));
        return lines;
    }

    static string RemoveWorktree(string repo, string worktree, string branch)
    {
        var shell = new Shell(repo);
        if (!Directory.Exists(worktree)) return $"The worktree {worktree} does not exist.";
        if (SamePath(worktree, repo)) return $"The folder {worktree} is the repository root, not a worktree, so it stays.";
        if (!File.Exists(Path.Combine(worktree, ".git"))) return $"The folder {worktree} has no .git file, so it is not a linked worktree and it stays.";
        if (new Shell(worktree).Git("status", "--porcelain") is { Ok: true } status && status.Text.Length > 0)
            return $"The worktree {worktree} has changes that are not committed, so it stays.";
        var error = "";
        var removeRan = false;
        foreach (var delay in RemoveDelays.Prepend(TimeSpan.Zero))
        {
            Thread.Sleep(delay);
            if (File.Exists(Path.Combine(worktree, ".git")))
            {
                var remove = shell.Git("worktree", "remove", worktree);
                removeRan = true;
                if (!remove.Ok) error = remove.Error.Trim();
            }
            if (removeRan && Directory.Exists(worktree) && !File.Exists(Path.Combine(worktree, ".git")))
            {
                try
                {
                    Directory.Delete(worktree, recursive: true);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    error = e.Message;
                }
            }
            if (!Directory.Exists(worktree))
            {
                shell.Git("worktree", "prune");
                return $"Removed the worktree {worktree}. The branch {branch} stays.";
            }
        }
        return $"The worktree {worktree} stays: {error}";
    }

    static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static IReadOnlyList<TimeSpan> RemoveDelays { get; set; } =
        [TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    public static (string Repo, string Path, string Branch) WorktreePlan(string cwd, string id)
    {
        var repo = new Shell(cwd).MustGit("rev-parse", "--show-toplevel").Replace('\\', '/');
        var shortId = id.Length > 8 ? id[..8] : id;
        var parent = Path.GetDirectoryName(repo.TrimEnd('/')) ?? repo;
        var path = Path.Combine(parent, Path.GetFileName(repo.TrimEnd('/')) + WorktreeSuffix, $"orch-{shortId}").Replace('\\', '/');
        return (repo, path, BranchPrefix + shortId);
    }

    public Meta MakeWorktree(string id)
    {
        var meta = store.Meta(id);
        if (meta.Worktree != true || meta.Repo is not null || meta.Cwd is not { } cwd) return meta;
        var (repo, path, branch) = WorktreePlan(cwd, id);
        var shell = new Shell(repo);
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var args = shell.BranchExists(branch) ? new[] { "worktree", "add", path, branch } : ["worktree", "add", "-b", branch, path];
            shell.MustGit(args);
        }
        var updated = meta with { Repo = repo, Cwd = path, Branch = branch };
        store.SaveMeta(updated);
        store.Events.Append(EventKinds.BranchCreate, Parties.Supervisor, Parties.Session(id), Messages.Fields(("text", $"worktree {path} on {branch}"), ("path", path), ("branch", branch), ("repo", repo)), null, id);
        return updated;
    }

    public Meta Enable(string id, string cwd, string? title, string? parent, string by)
    {
        var old = store.Has(id, StoreFiles.Meta) ? store.Meta(id) : null;
        var meta = (old ?? new Meta(id) { Cwd = cwd, CreatedBy = by, CreatedAt = store.Now, Started = true }) with
        {
            Title = title ?? old?.Title ?? $"Chat in {Path.GetFileName(cwd.TrimEnd('/', '\\'))}",
            Parent = parent ?? old?.Parent,
            Imported = true,
        };
        store.SaveMeta(meta);
        store.SetEnabled(id, true);
        store.Events.Append(EventKinds.SessionStart, parent is null ? Parties.User : Parties.Session(parent), Parties.Session(id), Messages.Fields(("text", $"{meta.Title} enabled"), ("title", meta.Title)), parent is null ? null : Messages.Fields(("parent", parent)), id);
        return meta;
    }

    public bool Import(string id, TimeSpan? wait = null)
    {
        var meta = store.Meta(id);
        var command = provider.OpenCommand(id, store.Cwd(id));
        new Shell(store.Cwd(id) ?? Environment.CurrentDirectory).Must(command[0], [.. command.Skip(1)]);
        var appId = provider.AppId(id);
        var end = DateTime.UtcNow + (wait ?? TimeSpan.FromSeconds(30));
        while (!provider.Read(appId).InApp && DateTime.UtcNow < end) Thread.Sleep(1000);
        if (!provider.Read(appId).InApp)
        {
            store.Log($"{id} import not confirmed: the app has no record {appId}");
            return false;
        }
        store.SaveMeta(store.Meta(id) with { Imported = true, AppId = appId });
        store.Events.Append(EventKinds.SessionImport, Parties.Supervisor, Parties.Session(id), Messages.Fields(("text", $"{meta.Title ?? id} imported into the desktop app"), ("appId", appId)), null, id);
        return true;
    }

    public IReadOnlyList<string> ArchiveInApp(IReadOnlyList<string> ids, TimeSpan wait, TimeSpan? poll = null)
    {
        var asked = new List<(string Id, string AppId)>();
        foreach (var id in ids)
        {
            if (store.Meta(id).AppId is not { } appId || !store.IsLive(id, store.Clock.GetUtcNow()) || provider.Read(appId).Archived) continue;
            store.Events.Post(new PostInput(EventKinds.Notice, Parties.Supervisor, Parties.Session(id), store.Texts.Notice("archive-self", [.. provider.AppTools])) { Session = id });
            asked.Add((id, appId));
        }
        var end = DateTime.UtcNow + wait;
        while (asked.Any(a => !provider.Read(a.AppId).Archived) && DateTime.UtcNow < end) Thread.Sleep(poll ?? TimeSpan.FromSeconds(1));
        return [.. asked.Select(a => provider.Read(a.AppId).Archived
            ? $"The app archived {a.Id[..Math.Min(8, a.Id.Length)]} (the session archived itself)."
            : $"The app still shows {a.Id[..Math.Min(8, a.Id.Length)]}: it did not archive itself in {wait.TotalSeconds:0} s. Archive it from the sidebar.")];
    }

    public IReadOnlyList<string> TreeOf(string id) =>
        [.. store.AllSessions().Where(s => store.Meta(s).Parent == id && Control(s).State != ControlStates.Archived).SelectMany(TreeOf), id];

    public IReadOnlyList<string> ArchiveTree(string id, string by, bool force)
    {
        var lines = new List<string>();
        foreach (var child in store.AllSessions().Where(s => store.Meta(s).Parent == id && Control(s).State != ControlStates.Archived))
            lines.AddRange(ArchiveTree(child, by, force));
        lines.AddRange(Archive(id, by, force));
        return lines;
    }
}
