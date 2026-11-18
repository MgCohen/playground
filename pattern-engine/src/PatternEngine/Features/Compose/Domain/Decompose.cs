using System.Text;
using System.Text.Json;
using PatternEngine.Plugins.Agents;

namespace PatternEngine.Features.Compose.Domain;

/// <summary>One typed need of a request: a kind and a name from the rules of the pack, and an amount when the kind has one.</summary>
public sealed record Need(string Kind, string Name, int? Amount);

/// <summary>
/// The spec role of all packs (R2): the decomposer agent turns the text of a request into typed needs. The pack gives its rules
/// file and its known names, and its code checks each need and writes the anchor. A missing value goes to the ask step.
/// </summary>
public static class Decompose
{
    public const string Agent = "decomposer";

    /// <summary>The input of one decomposer call. Context is more text of the pack with its title, for example the lore of the party pack.</summary>
    public static string Input(string rules, string text, IReadOnlyDictionary<string, IReadOnlyList<string>> known, (string Title, string Text)? context, IReadOnlyList<string> errors)
    {
        var b = new StringBuilder();
        b.Append("# Text\n\n").Append(text.Trim()).Append("\n\n# Rules\n\n").Append(rules.Trim()).Append("\n\n# Known names\n");
        foreach (var (kind, names) in known)
        {
            b.Append("\n- ").Append(kind).Append(": ").Append(string.Join(", ", names));
        }

        b.Append('\n');
        if (context is { } c && !string.IsNullOrWhiteSpace(c.Text))
        {
            b.Append("\n# ").Append(c.Title).Append("\n\n").Append(c.Text.Trim()).Append('\n');
        }

        if (errors.Count > 0)
        {
            b.Append("\n# Errors in your last answer\n\n");
            foreach (var e in errors)
            {
                b.Append("- ").Append(e).Append('\n');
            }
        }

        return b.ToString();
    }

    /// <summary>Reads the needs and the missing values of one answer. Kinds are trimmed and in lower case; names too, when lowerNames is true.</summary>
    public static (IReadOnlyList<Need> Needs, IReadOnlyList<string> Missing) Read(AgentAnswer answer, bool lowerNames = true)
    {
        var needs = new List<Need>();
        if (answer.Field("needs") is { ValueKind: JsonValueKind.Array } list)
        {
            foreach (var n in list.EnumerateArray().Where(n => n.ValueKind == JsonValueKind.Object))
            {
                var kind = Text(n, "kind").Trim().ToLowerInvariant();
                var name = Text(n, "name").Trim();
                int? amount = n.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetInt32(out var v) ? v : null;
                needs.Add(new Need(kind, lowerNames ? name.ToLowerInvariant() : name, amount));
            }
        }

        return (needs, answer.Texts("missing").Where(m => m.Trim().Length > 0).ToList());
    }

    private static string Text(JsonElement item, string field) =>
        item.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : string.Empty;
}
