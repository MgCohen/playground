using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PatternEngine.Features.Compose.Domain.Shared;
using PatternEngine.Plugins.Agents;
using PatternEngine.Plugins.Settings;

namespace PatternEngine.Domains.Party;

/// <summary>The ranges that code accepts for the amounts of a decomposer answer, from party.decomposer.ranges.</summary>
public sealed record DecomposeRanges(int StatMin, int StatMax, int BudgetMin, int BudgetMax)
{
    public static DecomposeRanges From(Settings decomposer)
    {
        var r = decomposer.Section("ranges");
        var stat = r.List("stat_min");
        var budget = r.List("max_cost");
        return new DecomposeRanges(Int(stat[0]), Int(stat[1]), Int(budget[0]), Int(budget[1]));
    }

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
}

public sealed record Decomposition(
    [property: JsonPropertyName("anchor_rubrics")] IReadOnlyList<string> Rubrics,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("lore")] bool Lore,
    [property: JsonPropertyName("attempts")] int Attempts)
{
    [JsonIgnore]
    public Anchor? Anchor { get; init; }
}

/// <summary>
/// The decomposer step of rung 6. The agent turns prose into typed needs. Code checks each need against the known
/// tags, stats and ranges, and writes the anchor: code makes each rubric id, ask and args.
/// </summary>
public static class Decomposer
{
    public const string Agent = "decomposer";

    public static string Input(PartyPack pack, string text, bool lore, IReadOnlyList<string> errors)
    {
        var b = new StringBuilder();
        b.Append("# Text\n\n").Append(text).Append("\n\n# Known tags\n\n").Append(string.Join(", ", pack.Words.Tags))
            .Append("\n\n# Known stats\n\n").Append(string.Join(", ", pack.Words.Stats)).Append('\n');
        if (lore)
        {
            b.Append("\n# Lore\n\n").Append(File.ReadAllText(Path.Combine(pack.Dir, "lore.md")).Trim()).Append('\n');
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

    /// <summary>Checks one answer. With no errors, it gives the anchor of the quest: the id of the case, the text as context, and one rubric for each need.</summary>
    public static (Anchor? Anchor, IReadOnlyList<string> Errors) Build(PartyPack pack, DecomposeRanges ranges, string id, string text, AgentAnswer answer)
    {
        if (!answer.Ok)
        {
            return (null, [$"no answer: {answer.Error}"]);
        }

        var errors = new List<string>();
        var tags = answer.Texts("tags").Select(t => t.Trim().ToLowerInvariant()).ToList();
        errors.AddRange(tags.Where(t => !pack.Words.Tags.Contains(t)).Select(t => $"unknown tag \"{t}\""));
        errors.AddRange(tags.GroupBy(t => t).Where(g => g.Count() > 1).Select(g => $"tag \"{g.Key}\" is in the list more than once"));
        if (tags.Count == 0)
        {
            errors.Add("no tags");
        }

        var stats = new List<(string Stat, int Min)>();
        if (answer.Field("stats") is { ValueKind: JsonValueKind.Array } list)
        {
            foreach (var s in list.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.Object))
            {
                var stat = s.TryGetProperty("stat", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim().ToLowerInvariant() : string.Empty;
                var min = s.TryGetProperty("min", out var m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out var v) ? v : int.MinValue;
                if (!pack.Words.Stats.Contains(stat))
                {
                    errors.Add($"unknown stat \"{stat}\"");
                }
                else if (min < ranges.StatMin || min > ranges.StatMax)
                {
                    errors.Add($"stat \"{stat}\": the amount must be from {ranges.StatMin} to {ranges.StatMax}");
                }
                else if (stats.Any(x => x.Stat == stat))
                {
                    errors.Add($"stat \"{stat}\" is in the list more than once");
                }
                else
                {
                    stats.Add((stat, min));
                }
            }
        }

        int? budget = null;
        if (answer.Field("budget") is { ValueKind: JsonValueKind.Number } bv)
        {
            if (!bv.TryGetInt32(out var b) || b < ranges.BudgetMin || b > ranges.BudgetMax)
            {
                errors.Add($"budget: the amount must be from {ranges.BudgetMin} to {ranges.BudgetMax}");
            }
            else
            {
                budget = b;
            }
        }

        return errors.Count > 0 ? (null, errors) : (Anchor(id, text, tags, stats, budget), []);
    }

    /// <summary>Writes the anchor of a quest from typed needs. The same words as the case files.</summary>
    public static Anchor Anchor(string id, string text, IReadOnlyList<string> tags, IReadOnlyList<(string Stat, int Min)> stats, int? budget)
    {
        var b = new StringBuilder();
        b.Append(CultureInfo.InvariantCulture, $"---\nid: {id}\nkind: success\nlevel: case\nscore: {{ rule: all }}\n---\n{text}\n\n");
        foreach (var t in tags)
        {
            b.Append(CultureInfo.InvariantCulture, $"- id: {t}\n  ask: Does the party have {("aeiou".Contains(t[0], StringComparison.Ordinal) ? "an" : "a")} {t}?\n  check: grader\n  args: {{ tag: {t} }}\n");
        }

        foreach (var (stat, min) in stats)
        {
            b.Append(CultureInfo.InvariantCulture, $"- id: {stat}\n  ask: Is the total {stat} of the party {min} or more?\n  check: grader\n  args: {{ stat: {stat}, min: {min} }}\n");
        }

        if (budget is { } max)
        {
            b.Append(CultureInfo.InvariantCulture, $"- id: budget\n  ask: Is the total cost of the party {max} or less?\n  check: grader\n  args: {{ max_cost: {max} }}\n");
        }

        return Features.Compose.Domain.Shared.Anchor.Parse(b.ToString(), $"decomposed:{id}");
    }

    /// <summary>The hidden anchor of a prose case, with its rubrics visible to the run: the control mode "given".</summary>
    public static Anchor Given(PartyCase c) =>
        c.Anchor with { Context = c.Text ?? string.Empty, Rubrics = c.Anchor.Rubrics.Select(r => r with { Visibility = "run" }).ToList() };

    public static string Describe(Anchor anchor) =>
        string.Join(", ", anchor.Rubrics.Select(r => r.Args.ContainsKey("tag") ? r.Id : r.Args.ContainsKey("stat") ? $"{r.Id} {r.Args["min"]}" : $"{r.Id} {r.Args["max_cost"]}"));
}
