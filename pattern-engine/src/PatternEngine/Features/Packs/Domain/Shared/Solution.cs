namespace PatternEngine.Features.Packs.Domain.Shared;

/// <summary>
/// A solution: the output of a request that uses more than one pattern. Each pattern stays complete; code joins them by their ports.
/// The top block of a joined output has the reserved pattern id <see cref="Id"/>.
/// </summary>
public static class Solution
{
    public const string Id = "solution";
}

/// <summary>One pattern of a solution: its item id (s1, s2, ...), its pattern id and its composed fragment.</summary>
public sealed record SolutionItem(string Id, string Pattern, string Fragment);

/// <summary>A wire between two items of a solution, for example s1.event to s2.event.</summary>
public sealed record JoinWire(string From, string To);

/// <summary>The wires of a solution and its outer ports. An outer port name is item_port, for example s1_request.</summary>
public sealed record JoinPlan(IReadOnlyList<JoinWire> Wires, IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs);
