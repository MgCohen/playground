using System.Text;
using PatternEngine.Features.Trace.Domain.Shared;
using PatternEngine.Plugins.Agents;

namespace PatternEngine.Features.Engine.Domain;

/// <summary>
/// The escalation step: an agent with read tools gets the node, the state and the reason, when a trigger fires.
/// It gives a decision (code checks it as usual), a no-fit, or a question for the user.
/// </summary>
public static class EscalateStep
{
    public const string ActionDecide = "decide";
    public const string ActionQuestion = "question";

    /// <summary>Returns true if the node has a decision after the escalation.</summary>
    public static async Task<bool> RunAsync(SolveContext s, WorkNode n, EscalationReason reason, CancellationToken ct)
    {
        if (n.Escalations >= s.Settings.Node.Escalations)
        {
            n.Reason = $"{n.Reason} (escalation limit)";
            return false;
        }

        n.Escalations++;
        s.Note($"node {n.Id}: escalation ({Reason(reason)}): {n.Reason}");
        var answer = await s.Agents.CallAsync("escalation", Input(s, n, reason), ct);
        if (!answer.Ok)
        {
            n.Reason = $"escalation gave no answer: {answer.Error}";
            return false;
        }

        if (answer.Text("action") == ActionQuestion)
        {
            return await QuestionAsync(s, n, answer, ct);
        }

        var d = DecideStep.InScope(n, Decision.From(answer));
        var problem = DecideStep.Apply(s, n, d);
        if (problem is null)
        {
            n.Reason = $"escalation: {d.Reason}";
            return true;
        }

        n.Status = NodeStatus.Unresolved;
        n.Reason = $"escalation: {problem}";
        return false;
    }

    private static async Task<bool> QuestionAsync(SolveContext s, WorkNode n, AgentAnswer answer, CancellationToken ct)
    {
        var q = answer.Field("question");
        string Text(string field) => q is { } e && e.TryGetProperty(field, out var v) ? v.ToString() : string.Empty;
        var options = q is { } o && o.TryGetProperty("options", out var opts) && opts.ValueKind == System.Text.Json.JsonValueKind.Array
            ? opts.EnumerateArray().Select(x => x.ToString()).ToList()
            : [];
        var question = new Question
        {
            Node = n.Id,
            About = string.IsNullOrEmpty(Text("about")) ? QuestionTopics.PatternChoice : Text("about"),
            Key = Text("key"),
            Text = Text("text"),
            Options = options,
        };
        var reply = await AskStep.AskAsync(s, question, ct);
        if (reply is null)
        {
            n.Status = NodeStatus.Asking;
            n.Reason = $"asked: {question.Text}";
            return false;
        }

        // The answer goes back into the queue: the node is decided again with the answer in its text.
        var again = s.Add($"{n.Text} (The user answered: {reply})", n.Allowed, n.Depth, n.Parent);
        again.Escalations = n.Escalations;
        n.Status = NodeStatus.Unresolved;
        n.Reason = "replaced by the answer of the user";
        var reason = await DecideStep.RunAsync(s, again, ct);
        if (reason is not null)
        {
            await RunAsync(s, again, reason.Value, ct);
        }

        Replace(s, n, again);
        return n.Status is NodeStatus.Solved or NodeStatus.PartialFit or NodeStatus.None;
    }

    /// <summary>Moves the decision of the new node into the old node, so the parent keeps its child id.</summary>
    private static void Replace(SolveContext s, WorkNode old, WorkNode fresh)
    {
        old.Status = fresh.Status;
        old.Pattern = fresh.Pattern;
        old.Variant = fresh.Variant;
        old.Params.Clear();
        foreach (var (k, v) in fresh.Params)
        {
            old.Params[k] = v;
        }

        old.Children.Clear();
        old.Children.AddRange(fresh.Children);
        old.Open.Clear();
        old.Open.AddRange(fresh.Open);
        old.Nearest = fresh.Nearest;
        old.Deltas.Clear();
        old.Deltas.AddRange(fresh.Deltas);
        old.NewId = fresh.NewId;
        old.Reason = fresh.Reason;
        fresh.Status = NodeStatus.Unresolved;
        fresh.Reason = $"merged into node {old.Id}";
        fresh.Children.Clear();
        s.Note($"node {old.Id}: decided again with the answer of the user");
    }

    private static string Input(SolveContext s, WorkNode n, EscalationReason reason)
    {
        var path = new StringBuilder();
        for (int? p = n.Parent; p is not null; p = s.Nodes[p.Value].Parent)
        {
            var parent = s.Nodes[p.Value];
            path.Insert(0, $"- node {parent.Id}: \"{parent.Text}\" → {parent.Status} {parent.Pattern}\n");
        }

        return $"""
            Reason: {Reason(reason)}. {n.Reason}

            Request part: {n.Text}
            {DecideStep.Remainder(s, n)}

            Parent nodes:
            {(path.Length > 0 ? path.ToString() : "- none (this is the full request)\n")}
            Allowed patterns: {string.Join(", ", n.Allowed)}

            Candidates:
            {s.Candidates(n.Allowed)}

            The pattern files are in patterns/. The subject rules are in instructions.md.
            """;
    }

    private static string Reason(EscalationReason reason) => reason switch
    {
        EscalationReason.Unclear => "the decider could not choose (unclear)",
        EscalationReason.NearTie => "two candidates are almost equal",
        EscalationReason.NotAllowed => "the decision is not allowed or the node is too deep",
        EscalationReason.RetriesUsedUp => "the compose check failed after all retries",
        EscalationReason.OutOfDomain => "the request may be outside the domain",
        _ => reason.ToString(),
    };
}
