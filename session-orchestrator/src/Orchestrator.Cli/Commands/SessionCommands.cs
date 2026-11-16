using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Store;

namespace Orchestrator.Cli;

public static class SessionCommands
{
    public static int New(CommandContext c)
    {
        var a = c.Args;
        var brief = new BriefFields
        {
            Title = a.RequiredOption("title"),
            Goal = a.Option("goal") ?? (a.Option("goal-file") is { } file ? File.ReadAllText(file) : throw new UsageException("missing --goal or --goal-file")),
            Scope = a.Option("scope"),
            DoneWhen = a.Option("done-when"),
            Skills = a.List("skills"),
            Agent = a.Option("agent"),
            Cwd = Path.GetFullPath(a.Option("cwd") ?? c.Cwd).Replace('\\', '/'),
            Worktree = a.Flag("worktree") ? true : null,
            Parent = a.Option("parent") is { } parent ? c.Messages.RequireSession(parent) : null,
        };
        var id = c.Messages.NewSession(brief, a.Option("created-by") ?? "orch", brief.Parent is { } p ? Parties.Session(p) : Parties.User);
        c.Output.WriteLine(id);
        return 0;
    }

    public static int Send(CommandContext c)
    {
        var session = c.Session(1);
        var text = c.Args.Rest(2);
        if (text.Length == 0) throw new UsageException("missing <text>");
        var (inbox, _) = c.Messages.Send(session, text);
        c.Output.WriteLine($"Sent to {Short(session)} as inbox/{inbox}.");
        return 0;
    }

    public static int Tree(CommandContext c)
    {
        var store = c.Store;
        var all = store.AllSessions().ToHashSet();
        var metas = all.Select(store.Meta).ToList();
        var now = store.Clock.GetUtcNow();
        void Print(Meta meta, int depth)
        {
            c.Output.WriteLine($"{new string(' ', depth * 2)}{Line(store, meta, now)}");
            foreach (var child in metas.Where(m => m.Parent == meta.Id).OrderBy(m => m.CreatedAt)) Print(child, depth + 1);
        }
        foreach (var root in metas.Where(m => m.Parent is null || !all.Contains(m.Parent)).OrderBy(m => m.CreatedAt)) Print(root, 0);
        if (metas.Count == 0) c.Output.WriteLine("No sessions.");
        return 0;
    }

    public static int Status(CommandContext c)
    {
        var store = c.Store;
        var id = c.Session(1);
        var meta = store.Meta(id);
        var now = store.Clock.GetUtcNow();
        c.Output.WriteLine(Line(store, meta, now));
        c.Output.WriteLine($"  id: {id}");
        c.Output.WriteLine($"  cwd: {store.Cwd(id)}");
        if (meta.Parent is { } parent) c.Output.WriteLine($"  parent: {parent}");
        var lease = store.Lease(id);
        c.Output.WriteLine($"  lease: {lease.Holder} since {lease.Since}{(store.IsLive(id, now) ? " (live)" : "")}");
        if (store.Report(id) is { } report) c.Output.WriteLine($"  report: {report.State}: {report.Summary}");
        if (store.Blocked(id)?.Reason is { } blocked) c.Output.WriteLine($"  blocked: {blocked}");
        foreach (var d in store.OpenDecisions(id)) c.Output.WriteLine($"  open decision {d.Id} ({d.Risk}): {d.Question}");
        foreach (var entry in store.Stacks(id))
            foreach (var v in entry.Versions)
                c.Output.WriteLine($"  stack {v.Branch} on {v.Base}: {v.State}{(v.Pr is { } pr ? $", PR #{pr}" : "")}");
        return 0;
    }

    public static string Line(FileStore store, Meta meta, DateTimeOffset now)
    {
        var id = meta.Id;
        var state = store.Status(id)?.State ?? "new";
        var report = store.Report(id)?.State;
        var open = store.OpenDecisions(id).Count;
        var flags = new List<string> { store.Lease(id).Holder + (store.IsLive(id, now) ? " live" : ""), state };
        if (report is not null) flags.Add($"report {report}");
        if (open > 0) flags.Add($"{open} open");
        if (store.Blocked(id)?.Reason is not null) flags.Add("blocked");
        return $"{Short(id)} {meta.Title ?? "(no title)"} [{string.Join(", ", flags)}]";
    }

    public static string Short(string id) => id.Length > 8 ? id[..8] : id;
}
