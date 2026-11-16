using PatternEngine.Features.Trace.Domain.Shared;

namespace PatternEngine.Features.Engine.Domain;

public static class NodeStatus
{
    public const string Open = "open";
    public const string Solved = "solved";
    public const string Partial = "partial";
    public const string None = "none";
    public const string Asking = "asking";
    public const string Unresolved = "unresolved";
}

/// <summary>One node of the work graph: a part of the request, the patterns that are allowed for it, and the decision.</summary>
public sealed class WorkNode(int id, string text, IReadOnlyList<string> allowed, int depth, int? parent)
{
    public int Id { get; } = id;

    public string Text { get; } = text;

    public IReadOnlyList<string> Allowed { get; } = allowed;

    public int Depth { get; } = depth;

    public int? Parent { get; } = parent;

    public string Status { get; set; } = NodeStatus.Open;

    public string? Pattern { get; set; }

    public string? Variant { get; set; }

    public Dictionary<string, string> Params { get; } = new(StringComparer.Ordinal);

    public List<int> Children { get; } = [];

    /// <summary>Requirements that the pattern does not cover. Other patterns must cover them (a partial).</summary>
    public List<string> Open { get; } = [];

    /// <summary>True for a node made from an open requirement of its parent, not from a part.</summary>
    public bool FromOpen { get; set; }

    public string? Reason { get; set; }

    public string? Nearest { get; set; }

    public List<string> Deltas { get; } = [];

    public string? NewId { get; set; }

    public int Escalations { get; set; }

    public NodeState ToState() => new()
    {
        Id = Id,
        Parent = Parent,
        Depth = Depth,
        Text = Text,
        Status = Status,
        Pattern = Status == NodeStatus.None ? NewId : Pattern,
        Variant = Variant,
        Params = Params.Count > 0 ? new Dictionary<string, string>(Params) : null,
        Children = [.. Children],
        Open = [.. Open],
        Reason = Reason,
    };
}
