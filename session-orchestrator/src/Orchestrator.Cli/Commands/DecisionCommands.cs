using Orchestrator.Core.Policy;

namespace Orchestrator.Cli;

public static class DecisionCommands
{
    public static int List(CommandContext c)
    {
        var any = false;
        foreach (var session in c.Store.Sessions())
            foreach (var d in c.Store.OpenDecisions(session))
            {
                any = true;
                var title = c.Store.Meta(session).Title ?? SessionCommands.Short(session);
                c.Output.WriteLine($"{d.Id} [{d.Risk}{(d.DefaultOption is { } o ? $", default {o}" : "")}] {title}: {d.Question}");
                if (d.Options is { Length: > 0 } options) c.Output.WriteLine($"  options: {string.Join(", ", options)}");
            }
        if (!any) c.Output.WriteLine("No open decisions.");
        return 0;
    }

    public static int Answer(CommandContext c)
    {
        var decision = c.Args.Required(1, "decision");
        var text = c.Args.Rest(2);
        if (text.Length == 0) throw new UsageException("missing <text>");
        var session = c.Messages.SessionOfDecision(decision);
        c.Messages.Answer(session, decision, text);
        c.Output.WriteLine($"Answered {decision} in session {SessionCommands.Short(session)}.");
        return 0;
    }

    public static int Review(CommandContext c)
    {
        var decision = c.Args.Required(1, "decision");
        var verdict = c.Args.Required(2, "verdict");
        if (!Verdict.IsValid(verdict)) throw new UsageException("the verdict is approve, change or refuse");
        var session = c.Messages.SessionOfDecision(decision);
        var inbox = c.Messages.Review(session, decision, verdict, c.Args.Rest(3));
        c.Output.WriteLine($"Review {verdict} of {decision} sent to session {SessionCommands.Short(session)} as inbox/{inbox}.");
        return 0;
    }

    public static int Decision(CommandContext c)
    {
        var decision = c.Args.Required(2, "decision");
        switch (c.Args.Required(1, "start|pr|ready"))
        {
            case "start": c.Stack.Start(decision); break;
            case "pr": c.Stack.OpenPr(decision); break;
            case "ready": c.Stack.Ready(decision); break;
            default: throw new UsageException("orch decision start|pr|ready <decision>");
        }
        return 0;
    }

    public static int Stack(CommandContext c)
    {
        var decision = c.Args.Required(2, "decision");
        switch (c.Args.Required(1, "rebase|close"))
        {
            case "rebase": c.Stack.Rebase(decision); break;
            case "close": c.Stack.Close(decision, c.Args.Option("text")); break;
            default: throw new UsageException("orch stack rebase|close <decision>");
        }
        return 0;
    }

    public static int Merge(CommandContext c)
    {
        var guard = new Git.MergeGuard(c.Store, new Store.Shell(c.Cwd), c.Output);
        return guard.Merge(guard.Resolve(c.Args.Required(1, "pr or branch"), c.Args.Option("base")));
    }
}
