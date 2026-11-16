using System.Text.Json.Serialization;
using PatternEngine.Plugins.Yaml;

namespace PatternEngine.Features.Cases.Domain.Shared;

public static class Outcomes
{
    public const string Match = "match";
    public const string NoFit = "no-fit";
    public const string Ask = "ask";
    public const string Stopped = "stopped";
}

/// <summary>
/// What a correct run gives. Outcome is match, no-fit or ask. Asks lists the inputs that the engine must ask about.
/// Spec names a behavior that a pack can check by simulation, for domains where many trees are correct.
/// </summary>
public sealed record Expect
{
    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("tree")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TreeNode? Tree { get; init; }

    [JsonPropertyName("nearest")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Nearest { get; init; }

    [JsonPropertyName("asks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Asks { get; init; }

    /// <summary>The expected outcome of each remainder item of a partial fit, in any order. For example [no-fit].</summary>
    [JsonPropertyName("remainder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Remainder { get; init; }

    /// <summary>The expected output as typed data flow: the input types that the request gives and the output types that it needs.</summary>
    [JsonPropertyName("io")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IoSpec? Io { get; init; }

    [JsonPropertyName("spec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Spec { get; init; }

    public static Expect From(object? node)
    {
        var map = Node.Map(node);
        var nearest = map.Raw("nearest");
        return new Expect
        {
            Outcome = map.Get("outcome") ?? Outcomes.Match,
            Tree = TreeNode.From(map.Raw("tree")),
            Nearest = nearest is null ? null : Node.Text(nearest) is { } one ? [one] : Node.Strings(nearest).ToList(),
            Asks = map.ContainsKey("asks") ? Node.Strings(map.Raw("asks")).ToList() : null,
            Spec = map.Get("spec"),
            Io = IoSpec.From(map.Raw("io")),
            Remainder = map.ContainsKey("remainder") ? Node.Strings(map.Raw("remainder")).ToList() : null,
        };
    }
}

/// <summary>
/// The expected behavior of a case as typed data flow. A solution passes if it needs no other input types and each output type gets a value.
/// Many solutions can give the same output.
/// </summary>
public sealed record IoSpec
{
    [JsonPropertyName("inputs")]
    public required List<string> Inputs { get; init; }

    [JsonPropertyName("outputs")]
    public required List<string> Outputs { get; init; }

    public static IoSpec? From(object? node)
    {
        if (node is null)
        {
            return null;
        }

        var map = Node.Map(node);
        return new IoSpec { Inputs = Node.Strings(map.Raw("inputs")).ToList(), Outputs = Node.Strings(map.Raw("outputs")).ToList() };
    }
}

/// <summary>
/// One eval case. Hidden facts are answers that the request does not give: the simulated user gives them only when the engine asks.
/// </summary>
public sealed record Case
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("tags")]
    public required List<string> Tags { get; init; }

    [JsonPropertyName("request")]
    public required string Request { get; init; }

    [JsonPropertyName("expect")]
    public required Expect Expect { get; init; }

    [JsonPropertyName("hidden")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Hidden { get; init; }

    public static Case From(object? node, string source)
    {
        var map = Node.Map(node);
        var hidden = Node.Map(map.Raw("hidden"));
        return new Case
        {
            Id = map.Get("id") ?? throw new FormatException("a case needs an id"),
            Source = source,
            Tags = [source, .. Node.Strings(map.Raw("tags"))],
            Request = map.Get("request") ?? throw new FormatException("a case needs a request"),
            Expect = Expect.From(map.Raw("expect")),
            Hidden = hidden.Count > 0 ? hidden.ToDictionary(e => e.Key, e => Node.Text(e.Value) ?? string.Empty, StringComparer.Ordinal) : null,
        };
    }
}
