using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Trace.Domain.Shared;

namespace PatternEngine.Features.Engine.Domain;

public sealed record SolveOutcome(string Output, RunTrace Trace, bool Composed, IReadOnlyList<string> Failures);

/// <summary>
/// The engine loop. Code owns the order: a work queue of nodes, a decide step for each node (with ask, escalation and author
/// when they apply), then compose bottom-up, then the trace from the graph state.
/// </summary>
public static class Solver
{
    public static async Task<SolveOutcome> RunAsync(SolveContext s, string request, CancellationToken ct)
    {
        var root = s.Add(request, s.TopIds, 0, null);
        var queue = new List<WorkNode> { root };
        using var limit = new SemaphoreSlim(Math.Max(1, s.Settings.Concurrency));
        while (queue.Count > 0)
        {
            var level = queue;
            var before = s.Nodes.Count;
            await Task.WhenAll(level.Select(async n =>
            {
                await limit.WaitAsync(ct);
                try
                {
                    await DecideNodeAsync(s, n, ct);
                }
                finally
                {
                    limit.Release();
                }
            }));
            queue = s.Nodes.Skip(before).Where(n => n.Status == NodeStatus.Open).ToList();
        }

        Composed top;
        var slot = s.Pack.Pack.Layout.Slots(null, 1)[0];
        top = await ComposeStep.RunAsync(s, root, slot, ct);
        var output = top.Fragment.Length > 0 ? s.Pack.Pack.Layout.Wrap(top.Fragment) : string.Empty;
        return new SolveOutcome(output, TraceBuilder.Build(s, root), top.Ok, top.Failures);
    }

    private static async Task DecideNodeAsync(SolveContext s, WorkNode n, CancellationToken ct)
    {
        var reason = await DecideStep.RunAsync(s, n, ct);
        if (reason is not null)
        {
            await EscalateStep.RunAsync(s, n, reason.Value, ct);
        }

        if (n.Status is NodeStatus.Solved or NodeStatus.Partial)
        {
            await AskStep.RunAsync(s, n, ct);
        }
    }
}

/// <summary>Writes the trace from the graph state. No agent writes it, so it always matches what the engine did.</summary>
public static class TraceBuilder
{
    public static RunTrace Build(SolveContext s, WorkNode root)
    {
        var used = s.Nodes.Where(n => n.Pattern is not null || n.NewId is not null).Select(n => n.Status == NodeStatus.None ? n.NewId! : n.Pattern!).Distinct();
        var asking = s.Nodes.Any(n => n.Status == NodeStatus.Asking);
        var outcome = asking ? Outcomes.Ask
            : root.Status == NodeStatus.None ? Outcomes.NoFit
            : root.Status is NodeStatus.Solved or NodeStatus.Partial ? Outcomes.Match
            : Outcomes.Stopped;
        return new RunTrace
        {
            Outcome = outcome,
            Tree = root.Status is NodeStatus.Unresolved or NodeStatus.Open ? null : Tree(s, root),
            Nearest = root.Nearest,
            Deltas = [.. root.Deltas],
            Proposed = root.Status == NodeStatus.None ? $"patterns/{root.NewId}.md" : null,
            Questions = [.. s.Questions],
            OpenRequirements = s.Nodes.SelectMany(n => n.Open).ToList(),
            Attempts = s.Attempts,
            StopReason = s.Agents.StopReason ?? (outcome == Outcomes.Stopped ? root.Reason : null),
            Hashes = new Hashes
            {
                Catalog = s.Catalog.Hash,
                Patterns = used.Select(s.Catalog.Get).OfType<Catalog.Domain.Shared.Pattern>().ToDictionary(p => p.Id, p => p.Hash, StringComparer.Ordinal),
            },
            Notes = [.. s.Notes],
            Graph = s.Nodes.Select(n => n.ToState()).ToList(),
        };
    }

    private static TreeNode Tree(SolveContext s, WorkNode n)
    {
        var kids = n.Children.Select(i => s.Nodes[i]).Where(k => !k.FromOpen && k.Status is not (NodeStatus.Unresolved or NodeStatus.Open)).ToList();
        return new TreeNode
        {
            P = n.Status == NodeStatus.None ? n.NewId! : n.Pattern!,
            V = n.Variant ?? (n.Status == NodeStatus.None ? "plain" : null),
            Params = n.Params.Count > 0 ? new Dictionary<string, string>(n.Params) : null,
            Parts = kids.Count > 0 ? kids.Select(k => Tree(s, k)).ToList() : null,
        };
    }
}
