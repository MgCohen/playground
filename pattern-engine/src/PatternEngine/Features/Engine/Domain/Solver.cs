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

        var slot = s.Pack.Pack.Layout.Slots(null, 1)[0];
        var top = await ComposeStep.RunAsync(s, root, slot, ct);
        var failures = top.Failures.ToList();
        var fragment = top.Fragment;
        var ok = top.Ok;

        // The remainder of each partial fit: compose each item (the author agent fills a gap first), then join all into one solution.
        var gaps = s.Nodes.Where(n => n.FromOpen && n.Status is NodeStatus.Solved or NodeStatus.PartialFit or NodeStatus.None).ToList();
        if (gaps.Count > 0 && ok)
        {
            var parts = await Task.WhenAll(gaps.Select(g => ComposeStep.RunAsync(s, g, slot, ct)));
            var nodes = new[] { root }.Concat(gaps).ToList();
            var fragments = new[] { top }.Concat(parts).ToList();
            var items = nodes.Select((n, i) => new Packs.Domain.Shared.SolutionItem($"s{i + 1}", n.Status == NodeStatus.None ? n.NewId! : n.Pattern!, fragments[i].Fragment)).ToList();
            failures.AddRange(parts.SelectMany(p => p.Failures));
            if (parts.All(p => p.Ok))
            {
                var joined = await JoinStep.RunAsync(s, items, ct);
                failures.AddRange(joined.Failures);
                if (joined.Output is not null)
                {
                    fragment = joined.Output;
                    s.Solution.AddRange(nodes);
                }
                else
                {
                    s.Note(joined.Failures.FirstOrDefault() ?? "join: no output");
                }
            }

            ok = failures.Count == 0;
        }

        var output = fragment.Length > 0 ? s.Pack.Pack.Layout.Wrap(fragment) : string.Empty;
        return new SolveOutcome(output, TraceBuilder.Build(s, root), ok, failures);
    }

    private static async Task DecideNodeAsync(SolveContext s, WorkNode n, CancellationToken ct)
    {
        var reason = await DecideStep.RunAsync(s, n, ct);
        if (reason is not null)
        {
            await EscalateStep.RunAsync(s, n, reason.Value, ct);
        }

        if (n.Status is NodeStatus.Solved or NodeStatus.PartialFit)
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
            : root.Status is NodeStatus.Solved or NodeStatus.PartialFit ? Outcomes.Match
            : Outcomes.Stopped;
        return new RunTrace
        {
            Outcome = outcome,
            Tree = root.Status is NodeStatus.Unresolved or NodeStatus.Open ? null
                : s.Solution.Count > 0 ? new TreeNode { P = Packs.Domain.Shared.Solution.Id, V = "plain", Parts = s.Solution.Select(n => Tree(s, n)).ToList() }
                : Tree(s, root),
            Nearest = root.Nearest,
            Deltas = [.. root.Deltas],
            Proposed = root.Status == NodeStatus.None ? $"patterns/{root.NewId}.md" : null,
            Questions = [.. s.Questions],
            OpenRequirements = s.Nodes.SelectMany(n => n.Open).ToList(),
            Remainder = s.Nodes.Where(n => n.FromOpen).Select(n => Remainder(s, n)).ToList(),
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

    private static RemainderItem Remainder(SolveContext s, WorkNode n) => new()
    {
        Text = n.Text,
        Outcome = n.Status switch
        {
            NodeStatus.None => Outcomes.NoFit,
            NodeStatus.Asking => Outcomes.Ask,
            NodeStatus.Solved or NodeStatus.PartialFit => Outcomes.Match,
            _ => Outcomes.Stopped,
        },
        Pattern = n.Status == NodeStatus.None ? n.NewId : n.Pattern,
        Proposed = n.Status == NodeStatus.None ? $"patterns/{n.NewId}.md" : null,
        Joined = s.Solution.Contains(n),
    };

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
