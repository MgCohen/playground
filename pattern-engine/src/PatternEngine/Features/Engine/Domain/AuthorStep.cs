using System.Text;
using System.Text.Json;
using PatternEngine.Features.Catalog.Domain;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Plugins.Agents;

namespace PatternEngine.Features.Engine.Domain;

/// <summary>
/// The author step: when no pattern fits, an agent writes a proposed pattern from the nearest pattern, the deltas and the authoring guide.
/// Code writes the file, validates it and runs the novelty check. The pattern makes the output of this run only;
/// a person approves it before it enters the index.
/// </summary>
public static class AuthorStep
{
    public static async Task RunAsync(SolveContext s, WorkNode n, CancellationToken ct)
    {
        var nearest = s.Catalog.Get(n.Nearest ?? string.Empty);
        if (nearest is null)
        {
            n.Status = NodeStatus.Unresolved;
            n.Reason = "no nearest pattern for the proposed pattern";
            return;
        }

        string? text = null;
        var problems = new List<string>();
        for (var attempt = 0; attempt < 2 && text is null; attempt++)
        {
            var answer = await s.Agents.CallAsync("pattern-author", Input(s, n, nearest, problems), ct);
            if (!answer.Ok)
            {
                problems = [$"no answer: {answer.Error}"];
                continue;
            }

            var candidate = Render(answer, n.NewId!, nearest);
            var pattern = PatternParser.Parse(candidate, $"{n.NewId}.md");
            problems = CatalogRules.Validate(s.Catalog.With(pattern)).Where(f => f.Severity == Severity.Error && f.Where.StartsWith(n.NewId!, StringComparison.Ordinal)).Select(f => f.Message).ToList();
            if (problems.Count == 0)
            {
                text = candidate;
            }
        }

        if (text is null)
        {
            s.Note($"node {n.Id}: the author agent failed ({string.Join("; ", problems)}); code wrote a scaffold");
            text = Scaffold(n.NewId!, nearest);
        }

        Directory.CreateDirectory(s.PatternsDir);
        File.WriteAllText(Path.Combine(s.PatternsDir, $"{n.NewId}.md"), text);
        var written = PatternParser.Parse(text, $"{n.NewId}.md");
        s.AddPattern(written);
        var similar = Novelty(s.Catalog, written);
        if (similar is not null)
        {
            s.Note($"node {n.Id}: novelty check: \"{written.Id}\" has the same parts and variants as \"{similar}\"");
        }

        n.Variant = written.Variants.FirstOrDefault()?.Id ?? "plain";
    }

    /// <summary>The id of an existing pattern with the same part set and variant ids, or null if the proposed pattern is new.</summary>
    public static string? Novelty(PatternCatalog catalog, Pattern proposed)
    {
        static string Key(Pattern p) =>
            string.Join(",", p.Parts.Select(x => $"{x.Ref}:{x.Count}:{x.Only}").Order(StringComparer.Ordinal)) + "|" + string.Join(",", p.VariantIds.Order(StringComparer.Ordinal));
        var key = Key(proposed);
        return catalog.All.Where(p => p.Id != proposed.Id && p.Kind == proposed.Kind).FirstOrDefault(p => Key(p) == key)?.Id;
    }

    /// <summary>The code fallback: a copy of the nearest pattern with status proposed, without the parts of other variants.</summary>
    public static string Scaffold(string id, Pattern nearest)
    {
        var variant = nearest.Variants.FirstOrDefault()?.Id;
        var parts = nearest.Parts.Where(p => p.Only is null || p.Only == variant)
            .Select(p => $"  - {{ ref: {p.Ref}, count: \"{p.Count}\" }}");
        return $"""
            ---
            id: {id}
            kind: {nearest.Kind.ToString().ToLowerInvariant()}
            level: {nearest.Level}
            status: proposed
            phrase: a {id}
            use_when:
              - The request names a {id}.
            variants:
              - id: plain
                when: Always.
            parts:
            {string.Join("\n", parts)}
            related: [{nearest.Id}]
            asserts: []
            ---
            ## Intent
            A {id}. Started from `{nearest.Id}`.

            ## Rules
            - Do: start from the rules of `{nearest.Id}`.

            ## Rationale
            No pattern fit the request. This file records the nearest pattern and the changes.

            """;
    }

    private static string Input(SolveContext s, WorkNode n, Pattern nearest, IReadOnlyList<string> problems)
    {
        var guide = Path.Combine(s.Pack.Dir, "authoring.md");
        return $"""
            Request: {n.Text}

            New pattern id: {n.NewId}
            Nearest pattern: {nearest.Id}
            Changes from the nearest pattern: {(n.Deltas.Count > 0 ? string.Join("; ", n.Deltas) : "none listed")}

            Known patterns (a part must be one of these ids):
            {string.Join(", ", s.Catalog.Active.Select(p => $"{p.Id} ({p.Kind.ToString().ToLowerInvariant()}, level {p.Level})"))}

            Nearest pattern file:
            {s.PatternText(nearest.Id)}

            {(File.Exists(guide) ? "Authoring guide of this domain:\n" + File.ReadAllText(guide) : string.Empty)}
            {(problems.Count > 0 ? "The last draft failed the validator. Fix these problems:\n- " + string.Join("\n- ", problems) : string.Empty)}
            """;
    }

    private static string Render(AgentAnswer a, string id, Pattern nearest)
    {
        var sb = new StringBuilder();
        sb.Append($"---\nid: {id}\nkind: {a.Text("kind") ?? nearest.Kind.ToString().ToLowerInvariant()}\nlevel: {nearest.Level}\nstatus: proposed\n");
        sb.Append($"phrase: {Quote(a.Text("phrase") ?? $"a {id}")}\nuse_when:\n");
        foreach (var w in Fallback(a.Texts("use_when"), $"The request names a {id}."))
        {
            sb.Append($"  - {Quote(w)}\n");
        }

        sb.Append("variants:\n");
        var variants = Objects(a, "variants").ToList();
        if (variants.Count == 0)
        {
            sb.Append("  - id: plain\n    when: Always.\n");
        }

        foreach (var v in variants)
        {
            sb.Append($"  - id: {Get(v, "id") ?? "plain"}\n    when: {Quote(Get(v, "when") ?? "Always.")}\n");
        }

        sb.Append("parts:\n");
        foreach (var p in Objects(a, "parts"))
        {
            var only = Get(p, "only");
            sb.Append($"  - {{ ref: {Get(p, "ref")}, count: \"{Get(p, "count") ?? "1"}\"{(string.IsNullOrEmpty(only) ? string.Empty : $", only: {only}")} }}\n");
        }

        var related = a.Texts("related").Append(nearest.Id).Distinct();
        sb.Append($"related: [{string.Join(", ", related)}]\nasserts:\n");
        foreach (var x in a.Texts("asserts"))
        {
            sb.Append($"  - {Quote(x)}\n");
        }

        sb.Append($"---\n## Intent\n{a.Text("intent") ?? $"A {id}."}\n\n## Rules\n");
        foreach (var r in Fallback(a.Texts("rules"), $"Do: start from the rules of `{nearest.Id}`."))
        {
            sb.Append($"- {r}\n");
        }

        sb.Append($"\n## Rationale\n{a.Text("rationale") ?? "No pattern fit the request."}\n");
        return sb.ToString();
    }

    private static IEnumerable<string> Fallback(IReadOnlyList<string> items, string fallback) => items.Count > 0 ? items : [fallback];

    private static IEnumerable<JsonElement> Objects(AgentAnswer a, string field) =>
        a.Field(field) is { ValueKind: JsonValueKind.Array } arr ? arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object) : [];

    private static string? Get(JsonElement e, string field) => e.TryGetProperty(field, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null;

    private static string Quote(string s) => JsonSerializer.Serialize(s, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}
