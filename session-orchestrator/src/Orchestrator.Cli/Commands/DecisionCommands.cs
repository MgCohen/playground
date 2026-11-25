using Orchestrator.Core.Policy;

namespace Orchestrator.Cli;

public static class DecisionCommands
{
    public static int List(CommandContext c)
    {
        var any = false;
        var only = c.Args.Option("session") is { } prefix ? c.Messages.RequireSession(prefix) : null;
        var stream = c.Args.Option("stream");
        var enabled = c.Store.Sessions().ToHashSet();
        foreach (var d in c.Store.OpenDecisions().Where(d => enabled.Contains(d.Session) && (only is null || d.Session == only) && SessionCommands.InStream(c.Store.Session(d.Session), stream)))
        {
            any = true;
            var title = c.Store.Session(d.Session).Title ?? SessionCommands.Short(d.Session);
            c.Output.WriteLine($"{d.Id} [{d.Risk}{(d.DefaultOption is { } o ? $", default {o}" : "")}] {title}: {d.Question}");
            if (d.Options is { Length: > 0 } options) c.Output.WriteLine($"  options: {string.Join(", ", options)}");
        }
        if (!any) c.Output.WriteLine("No open questions.");
        return 0;
    }

    public static int Answer(CommandContext c)
    {
        var decision = c.Args.Required(1, "decision");
        var text = c.Args.Rest(2);
        if (text.Length == 0) throw new UsageException("missing <text>");
        var session = c.Messages.SessionOfDecision(decision);
        var via = c.Args.Option("via") is { } v ? EventCommands.Party(c, v) : null;
        c.Messages.Answer(session, decision, text, via: via);
        c.Output.WriteLine($"Answered {decision} in session {SessionCommands.Short(session)}.");
        return 0;
    }

    /// <summary>A line that the user typed in the chat of a session answers its open decision, only when exactly one is open.</summary>
    public static int ChatAnswer(CommandContext c)
    {
        var session = c.Session(1);
        var text = c.Args.Rest(2);
        if (text.Length == 0) throw new UsageException("missing <text>");
        var open = c.Store.OpenDecisions(session);
        if (open.Count != 1)
        {
            c.Output.WriteLine(open.Count == 0 ? "No open question: the chat line is not an answer." : $"{open.Count} open questions: the chat line answers none of them.");
            return 0;
        }
        // The session already has the words in its chat, so the answer is delivered at once: its ack stops a second delivery.
        var answer = c.Messages.Answer(session, open[0].Id, text, via: "chat");
        c.Store.Events.Ack(answer, Core.Events.Parties.Session(session), "chat");
        c.Output.WriteLine($"Answered {open[0].Id} from the chat.");
        return 0;
    }

    public static int Review(CommandContext c)
    {
        var decision = c.Args.Required(1, "decision");
        var verdict = c.Args.Required(2, "verdict");
        if (!Verdict.IsValid(verdict)) throw new UsageException("the verdict is approve, change or refuse");
        var session = c.Messages.SessionOfDecision(decision);
        var review = c.Messages.Review(session, decision, verdict, c.Args.Rest(3));
        c.Output.WriteLine($"Review {verdict} of {decision} sent to session {SessionCommands.Short(session)} as {review.Id}.");
        return 0;
    }

    /// <summary>The user inbox: the open cards, or accept and refuse of one card.</summary>
    public static int Inbox(CommandContext c)
    {
        var inbox = new Store.Inbox(c.Store);
        switch (c.Args.Positional.ElementAtOrDefault(1))
        {
            case null:
                var cards = inbox.Cards();
                foreach (var card in cards)
                    c.Output.WriteLine($"{card.Envelope.Id} {card.Kind} [{card.Needs}] {card.Title}: {card.Envelope.Subject}");
                if (cards.Count == 0) c.Output.WriteLine("The inbox is empty.");
                return 0;
            case "accept":
                var accepted = inbox.Accept(c.Args.Required(2, "id"), c.Args.Rest(3));
                c.Output.WriteLine($"Accepted: {accepted.Kind} {accepted.Id}.");
                return 0;
            case "refuse":
                var refused = inbox.Refuse(c.Args.Required(2, "id"), c.Args.Rest(3));
                c.Output.WriteLine($"Refused: {refused.Kind} {refused.Id}.");
                return 0;
            default:
                throw new UsageException("orch inbox [accept|refuse <id> [text]]");
        }
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
