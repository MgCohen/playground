using Orchestrator.Core.Contract;
using Orchestrator.Core.Workflows;
using Orchestrator.Store;

namespace Orchestrator.Cli;

/// <summary><c>orch run</c>: start, show, list, step and stop workflow runs (C2).</summary>
public static class RunCommands
{
    const string OutputPrefix = "output-";
    const string InputPrefix = "input-";

    public static int Run(CommandContext c)
    {
        var verb = c.Args.Required(1, "start|show|list|step|stop");
        return verb switch
        {
            "start" => Start(c),
            "show" => Show(c),
            "list" => List(c),
            "step" => Step(c),
            "stop" => Stop(c),
            _ => throw new UsageException($"unknown run command {verb}"),
        };
    }

    static WorkflowRunner Runner(CommandContext c) => WorkflowHost.Runner(c.Store, EventCommands.DefaultSender(c));

    static int Start(CommandContext c)
    {
        var workflow = c.Args.Required(2, "workflow");
        var owner = c.Args.Option("owner") is { } o ? c.Messages.RequireSession(o)
            : c.Provider.CallerSessionId() is { Length: > 0 } id && c.Store.Registry.Exists(id) ? id
            : throw new UsageException("missing --owner: run this inside a session, or give --owner");
        var input = Prefixed(c, InputPrefix);
        if (c.Args.Option("goal") is { } goal) input["goal"] = goal;
        var run = Runner(c).Start(workflow, owner, input, EventCommands.DefaultSender(c));
        Print(c, run);
        return 0;
    }

    static int Show(CommandContext c)
    {
        var run = Runner(c).Require(c.Args.Required(2, "run"));
        if (c.Args.Flag("json"))
        {
            c.Output.WriteLine(StoreJson.Write(run));
            return 0;
        }
        Print(c, run);
        c.Output.WriteLine("History:");
        foreach (var (visit, n) in run.History.Select((v, i) => (v, i + 1)))
        {
            var parts = new List<string> { $"{n}. {visit.Step} ({visit.Executor})" };
            if (visit.Session is { } s) parts.Add($"session {SessionCommands.Short(s)}");
            if (visit.Holds.Count > 0) parts.Add($"holds: {string.Join("; ", visit.Holds)}");
            if (visit.Answer is { } a) parts.Add($"answer: {a}");
            if (visit.Approval is { } ap) parts.Add($"approval: {ap}");
            if (visit.Next is { } next) parts.Add($"next: {next}");
            c.Output.WriteLine("  " + string.Join(", ", parts));
        }
        return 0;
    }

    static int List(CommandContext c)
    {
        var owner = c.Args.Option("owner") is { } o ? c.Messages.RequireSession(o) : null;
        var open = c.Args.Flag("open");
        var runs = Runner(c).List(r => (owner is null || r.Owner == owner) && (!open || r.IsOpen));
        if (runs.Count == 0) c.Output.WriteLine("No runs.");
        foreach (var run in runs) c.Output.WriteLine($"{run.Id}  {run.Status,-8} step {run.Step}  owner {SessionCommands.Short(run.Owner)}");
        return 0;
    }

    static int Step(CommandContext c)
    {
        var a = c.Args;
        var input = new StepResult
        {
            Holds = a.Option("holds")?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [],
            Output = Prefixed(c, OutputPrefix),
            Pick = a.Option("pick"),
            Answer = a.Option("answer"),
            Verdict = a.Option("verdict"),
            SkipApproval = a.Flag("skip-approval"),
            Done = a.Flag("done"),
        };
        var run = Runner(c).Step(a.Required(2, "run"), input, EventCommands.DefaultSender(c));
        Print(c, run);
        return 0;
    }

    static int Stop(CommandContext c)
    {
        var run = Runner(c).Stop(c.Args.Required(2, "run"), c.Args.Option("reason") ?? "stopped by orch run stop", EventCommands.DefaultSender(c));
        Print(c, run);
        return 0;
    }

    static Dictionary<string, string> Prefixed(CommandContext c, string prefix) =>
        c.Args.Options.Where(p => p.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.Value is not null)
            .ToDictionary(p => p.Key[prefix.Length..], p => p.Value!);

    static void Print(CommandContext c, WorkflowRun run)
    {
        c.Output.WriteLine($"Run {run.Id} ({run.Workflow} v{run.Version}), owner {SessionCommands.Short(run.Owner)}: {run.Status}, step {run.Step}.");
        if (run.Wait is { } wait)
        {
            c.Output.WriteLine($"Waits ({wait.Kind}): {wait.Text}");
            if (wait.Options is { Count: > 0 } options) c.Output.WriteLine($"Options: {string.Join(", ", options)}");
            if (wait.Thread is { } thread) c.Output.WriteLine($"Question: {thread}");
        }
        if (run.Reason is { } reason) c.Output.WriteLine($"Reason: {reason}");
    }
}
