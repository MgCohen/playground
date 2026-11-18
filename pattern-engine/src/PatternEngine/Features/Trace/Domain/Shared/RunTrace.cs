using System.Text.Json.Serialization;
using PatternEngine.Features.Cases.Domain.Shared;

namespace PatternEngine.Features.Trace.Domain.Shared;

public static class QuestionTopics
{
    public const string Input = "input";
    public const string PatternChoice = "pattern choice";
    public const string Requirement = "requirement";
    public const string Domain = "domain";
}

/// <summary>One question to the user. Code checks About, Key and Options; the text is for the person.</summary>
public sealed record Question
{
    [JsonPropertyName("node")]
    public int Node { get; init; }

    [JsonPropertyName("about")]
    public required string About { get; init; }

    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("options")]
    public List<string> Options { get; init; } = [];

    [JsonPropertyName("answer")]
    public string? Answer { get; init; }
}

public sealed record NodeState
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("parent")]
    public int? Parent { get; init; }

    [JsonPropertyName("depth")]
    public int Depth { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("pattern")]
    public string? Pattern { get; init; }

    [JsonPropertyName("variant")]
    public string? Variant { get; init; }

    [JsonPropertyName("params")]
    public Dictionary<string, string>? Params { get; init; }

    [JsonPropertyName("children")]
    public List<int> Children { get; init; } = [];

    [JsonPropertyName("open")]
    public List<string> Open { get; init; } = [];

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

/// <summary>One requirement of the remainder of a partial fit: what other pattern covers it, or the proposed pattern that fills the gap.</summary>
public sealed record RemainderItem
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("pattern")]
    public string? Pattern { get; init; }

    [JsonPropertyName("proposed")]
    public string? Proposed { get; init; }

    /// <summary>True if the item is in the solution output, joined by its ports.</summary>
    [JsonPropertyName("joined")]
    public bool Joined { get; init; }
}

public sealed record Hashes
{
    [JsonPropertyName("catalog")]
    public required string Catalog { get; init; }

    [JsonPropertyName("patterns")]
    public required Dictionary<string, string> Patterns { get; init; }
}

/// <summary>What the engine did, as data. Code writes it from the graph state, so it always matches the output.</summary>
public sealed record RunTrace
{
    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("tree")]
    public TreeNode? Tree { get; init; }

    [JsonPropertyName("nearest")]
    public string? Nearest { get; init; }

    [JsonPropertyName("deltas")]
    public List<string> Deltas { get; init; } = [];

    [JsonPropertyName("proposed")]
    public string? Proposed { get; init; }

    [JsonPropertyName("questions")]
    public List<Question> Questions { get; init; } = [];

    [JsonPropertyName("open_requirements")]
    public List<string> OpenRequirements { get; init; } = [];

    [JsonPropertyName("remainder")]
    public List<RemainderItem> Remainder { get; init; } = [];

    [JsonPropertyName("attempts")]
    public int Attempts { get; init; }

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; init; }

    [JsonPropertyName("hashes")]
    public Hashes? Hashes { get; init; }

    [JsonPropertyName("notes")]
    public List<string> Notes { get; init; } = [];

    [JsonPropertyName("graph")]
    public List<NodeState> Graph { get; init; } = [];

    /// <summary>The spec and the compose loop, when engine.decider is compose. Null for the status decider.</summary>
    [JsonPropertyName("compose")]
    public ComposeTraceRecord? Compose { get; init; }
}

/// <summary>The compose part of a run trace: the spec rubrics, the spec errors, one score line for each anchor (G3), and the loop trace.</summary>
public sealed record ComposeTraceRecord
{
    [JsonPropertyName("spec")]
    public List<string> Spec { get; init; } = [];

    [JsonPropertyName("spec_errors")]
    public List<string> SpecErrors { get; init; } = [];

    [JsonPropertyName("scores")]
    public List<string> Scores { get; init; } = [];

    [JsonPropertyName("loop")]
    public object? Loop { get; init; }
}
