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
            Type = a.Option("type"),
            Stream = a.Option("stream"),
            Workflow = a.Option("workflow"),
            Cwd = Path.GetFullPath(a.Option("cwd") ?? c.Cwd).Replace('\\', '/'),
            Worktree = a.Flag("worktree") ? true : null,
            AllowDefaults = a.Flag("allow-defaults") ? true : null,
            Background = a.Flag("background") ? true : null,
            Where = a.Option("where"),
            Parent = a.Option("parent") is { } parent ? c.Messages.RequireSession(parent) : null,
        };
        var id = c.Messages.NewSession(brief, a.Option("created-by") ?? "orch", brief.Parent is { } p ? Parties.Session(p) : Parties.User);
        c.Output.WriteLine(id);
        return 0;
    }

    public static int Enable(CommandContext c)
    {
        var id = c.Args.Option("id") ?? c.Provider.CallerSessionId()
            ?? throw new UsageException($"no session id: run this inside a {c.Provider.Name} session, or give --id");
        var parent = c.Args.Option("parent") is { } p ? c.Messages.RequireSession(p) : null;
        var meta = new Lifecycle(c.Store).Enable(id, Path.GetFullPath(c.Cwd).Replace('\\', '/'), c.Args.Option("title"), parent, "orch enable");
        c.Output.WriteLine($"Enabled as {id} ({meta.Title}){(parent is not null ? $", child of {Short(parent)}" : "")}.");
        c.Output.WriteLine($"The plugin activates after this tool result: the {c.Provider.PluginTools} tools, the inbox and the live lease.");
        c.Output.WriteLine($"A child joins with: orch enable --parent {id} --title \"<title>\"");
        return 0;
    }

    public static int Send(CommandContext c)
    {
        var session = c.Session(1);
        var text = c.Args.Rest(2);
        if (text.Length == 0) throw new UsageException("missing <text>");
        var from = c.Args.Option("from") is { } f ? EventCommands.Party(c, f) : EventCommands.DefaultSender(c);
        var e = c.Messages.Send(session, text, from);
        c.Output.WriteLine($"Sent to {Short(session)} as {e.Id}.");
        return 0;
    }

    public static int Tree(CommandContext c)
    {
        var store = c.Store;
        var stream = c.Args.Option("stream");
        var metas = store.AllSessions().Select(store.Session).Where(m => InStream(m, stream)).ToList();
        var all = metas.Select(m => m.Id).ToHashSet();
        var now = store.Clock.GetUtcNow();
        void Print(SessionEntry meta, int depth)
        {
            c.Output.WriteLine($"{new string(' ', depth * 2)}{Line(store, meta, now)}");
            foreach (var child in metas.Where(m => m.Parent == meta.Id).OrderBy(m => m.Created?.At)) Print(child, depth + 1);
        }
        foreach (var root in metas.Where(m => m.Parent is null || !all.Contains(m.Parent)).OrderBy(m => m.Created?.At)) Print(root, 0);
        if (metas.Count == 0) c.Output.WriteLine(stream is null ? "No sessions." : $"No sessions in stream {stream}.");
        return 0;
    }

    /// <summary>True when there is no stream filter, or the session is in the stream (the registry entry, no case).</summary>
    public static bool InStream(SessionEntry meta, string? stream) =>
        stream is null || string.Equals(meta.Stream, stream, StringComparison.OrdinalIgnoreCase);

    /// <summary>The status of one session, or with <c>--stream</c> the status of each session of that stream.</summary>
    public static int Status(CommandContext c)
    {
        if (c.Args.Option("stream") is not { } stream) return Status(c, c.Session(1));
        if (c.Args.Positional.Count > 1) throw new UsageException("give a <session> or --stream, not both");
        var metas = c.Store.AllSessions().Select(c.Store.Session).Where(m => InStream(m, stream)).OrderBy(m => m.Created?.At, StringComparer.Ordinal).ToList();
        foreach (var meta in metas) Status(c, meta.Id);
        if (metas.Count == 0) c.Output.WriteLine($"No sessions in stream {stream}.");
        return 0;
    }

    static int Status(CommandContext c, string id)
    {
        var store = c.Store;
        var meta = store.Session(id);
        var now = store.Clock.GetUtcNow();
        c.Output.WriteLine(Line(store, meta, now));
        c.Output.WriteLine($"  id: {id}");
        c.Output.WriteLine($"  cwd: {store.Cwd(id)}");
        c.Output.WriteLine($"  code: {store.Registry.FullCode(id)}");
        if (meta.Stream is { } inStream) c.Output.WriteLine($"  stream: {inStream}");
        if (meta.Work.Branch is { } branch) c.Output.WriteLine($"  worktree: {meta.Work.Cwd} on {branch} (repository {meta.Work.Repo})");
        if (meta.Parent is { } parent) c.Output.WriteLine($"  parent: {parent}");
        var lease = store.Lease(id);
        c.Output.WriteLine($"  lease: {lease.Holder} since {lease.Since}{(store.IsLive(id, now) ? " (live)" : "")}");
        if (store.Report(id) is { } report) c.Output.WriteLine($"  report: {report.State}: {report.Summary}");
        if (store.Blocked(id)?.Reason is { } blocked) c.Output.WriteLine($"  blocked: {blocked}");
        foreach (var d in store.OpenDecisions(id)) c.Output.WriteLine($"  open question {d.Id} ({d.Risk}): {d.Question}");
        foreach (var entry in store.Stacks(id))
            foreach (var v in entry.Versions)
                c.Output.WriteLine($"  stack {v.Branch} on {v.Base}: {v.State}{(v.Pr is { } pr ? $", PR #{pr}" : "")}");
        return 0;
    }

    public static string Line(FileStore store, SessionEntry meta, DateTimeOffset now)
    {
        var id = meta.Id;
        var state = store.Status(id)?.State ?? "new";
        var report = store.Report(id)?.State;
        var open = store.OpenDecisions(id).Count;
        var flags = new List<string> { store.Lease(id).Holder + (store.IsLive(id, now) ? " live" : ""), state };
        if (report is not null) flags.Add($"report {report}");
        if (open > 0) flags.Add($"{open} open");
        if (store.Blocked(id)?.Reason is not null) flags.Add("blocked");
        if (new Lifecycle(store).Control(id).Phase is var control && control != Core.Contract.ControlStates.Active) flags.Add(control);
        return $"{Short(id)} {meta.Title ?? "(no title)"} [{string.Join(", ", flags)}]";
    }

    public static string Short(string id) => id.Length > 8 ? id[..8] : id;
}
