using System.Text.Json.Serialization;
using PatternEngine.Plugins.Yaml;

namespace PatternEngine.Features.Cases.Domain.Shared;

/// <summary>A pattern tree: pattern id, variant, params and child pattern trees. Leaf parts are not in a tree.</summary>
public sealed record TreeNode
{
    [JsonPropertyName("p")]
    public required string P { get; init; }

    [JsonPropertyName("v")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? V { get; init; }

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Params { get; init; }

    [JsonPropertyName("parts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TreeNode>? Parts { get; init; }

    public static TreeNode? From(object? node)
    {
        var map = Node.Map(node);
        var p = map.Get("p");
        if (p is null)
        {
            return null;
        }

        var parameters = Node.Map(map.Raw("params"));
        var parts = Node.List(map.Raw("parts")).Select(From).OfType<TreeNode>().ToList();
        return new TreeNode
        {
            P = p,
            V = map.Get("v"),
            Params = parameters.Count > 0 ? parameters.ToDictionary(e => e.Key, e => Node.Text(e.Value) ?? string.Empty, StringComparer.Ordinal) : null,
            Parts = map.ContainsKey("parts") ? parts : null,
        };
    }

    /// <summary>Short text form for reports: street(row)[house, house(chimney){door_fill:red}].</summary>
    public override string ToString()
    {
        var s = P + (V is null ? string.Empty : $"({V})");
        if (Params is { Count: > 0 })
        {
            s += "{" + string.Join(",", Params.Select(e => $"{e.Key}:{e.Value}")) + "}";
        }

        if (Parts is { Count: > 0 })
        {
            s += "[" + string.Join(", ", Parts) + "]";
        }

        return s;
    }

    /// <summary>The differences between an expected tree and an actual tree. Empty means a match.</summary>
    public static IReadOnlyList<string> Diff(TreeNode expected, TreeNode? actual, string at = "")
    {
        var here = at.Length > 0 ? $"{at} > {expected.P}" : expected.P;
        if (actual is null)
        {
            return [$"{here}: missing"];
        }

        if (expected.P != actual.P)
        {
            return [$"{here}: expected \"{expected.P}\", got \"{actual.P}\""];
        }

        var diffs = new List<string>();
        if (expected.V is not null && expected.V != actual.V)
        {
            diffs.Add($"{here}: expected variant \"{expected.V}\", got \"{actual.V ?? "-"}\"");
        }

        foreach (var (key, value) in expected.Params ?? [])
        {
            var got = actual.Params?.GetValueOrDefault(key);
            if (got is null || !string.Equals(got, value, StringComparison.OrdinalIgnoreCase))
            {
                diffs.Add($"{here}: param {key} expected \"{value}\", got \"{got ?? "-"}\"");
            }
        }

        if (expected.Parts is not null)
        {
            var a = actual.Parts ?? [];
            if (a.Count != expected.Parts.Count)
            {
                diffs.Add($"{here}: expected {expected.Parts.Count} parts [{string.Join(", ", expected.Parts.Select(x => x.P))}], got {a.Count} [{string.Join(", ", a.Select(x => x.P))}]");
            }
            else
            {
                for (var i = 0; i < a.Count; i++)
                {
                    diffs.AddRange(Diff(expected.Parts[i], a[i], $"{here}[{i}]"));
                }
            }
        }

        return diffs;
    }

    /// <summary>The same tree with patterns and variants only.</summary>
    public TreeNode Shape() => new() { P = P, V = V, Parts = Parts?.Select(x => x.Shape()).ToList() ?? [] };
}
