using System.Text.Json.Serialization;
using PatternEngine.Features.Compose.Domain;
using PatternEngine.Features.Compose.Domain.Shared;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Plugins.Yaml;

namespace PatternEngine.Features.Engine.Domain;

/// <summary>One grader fixture: a spec from typed needs, a set of entries, and the known verdict of each grader rubric.</summary>
public sealed record GraderFixture(string Id, string Request, Anchor Spec, IReadOnlyList<string> Set, IReadOnlyDictionary<string, bool> Expect);

public sealed record FixtureLine(
    [property: JsonPropertyName("fixture")] string Fixture,
    [property: JsonPropertyName("set")] IReadOnlyList<string> Set,
    [property: JsonPropertyName("right")] int Right,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("wrong")] IReadOnlyList<string> Wrong,
    [property: JsonPropertyName("error")] string? Error);

/// <summary>
/// The grader fixtures of an engine pack with compose hooks (G2): grader-fixtures.yaml in the pack folder. Each fixture has a known
/// answer. Run them when the model or a grader file changes; the run writes the stamp (B8).
/// </summary>
public static class GraderCheck
{
    public const string FixtureFile = "grader-fixtures.yaml";

    /// <summary>The files that make the grader of a pack: the agent file (the copy in the pack, if it has one) and the rules file of the pack.</summary>
    public static IReadOnlyList<string> GraderFiles(PackContext pack, string agentsDir)
    {
        var own = Path.Combine(pack.AgentsDir, $"{ComposeLoop.GraderAgent}.md");
        return [File.Exists(own) ? own : Path.Combine(agentsDir, $"{ComposeLoop.GraderAgent}.md"), Path.Combine(pack.Dir, "grading.md")];
    }

    /// <summary>Loads the fixtures. Code checks each spec, each entry and each expected rubric; a problem is an error.</summary>
    public static IReadOnlyList<GraderFixture> Load(PackContext pack)
    {
        var hooks = pack.Pack.Compose ?? throw new InvalidOperationException($"the pack \"{pack.Pack.Name}\" has no compose hooks");
        var file = Path.Combine(pack.Dir, FixtureFile);
        var fixtures = new List<GraderFixture>();
        var entries = EngineComposeTask.Entries(pack.Catalog).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var m in Node.List(Yaml.Parse(File.ReadAllText(file))).Select(Node.Map))
        {
            var id = m.Get("id") ?? "?";
            var needs = Node.List(m.Raw("needs")).Select(Node.Map)
                .Select(n => new Need(n.Get("kind") ?? string.Empty, n.Get("name") ?? string.Empty, int.TryParse(n.Get("amount"), out var a) ? a : null)).ToList();
            var (spec, errors) = hooks.Spec(id, m.Get("request") ?? string.Empty, needs, pack.Catalog);
            if (spec is null)
            {
                throw new InvalidOperationException($"{FixtureFile}: fixture {id}: {string.Join("; ", errors)}");
            }

            var set = Node.Strings(m.Raw("set")).ToList();
            var unknown = set.Where(e => !entries.Contains(e)).ToList();
            var expect = Node.Map(m.Raw("expect")).ToDictionary(kv => kv.Key, kv => bool.Parse(kv.Value?.ToString() ?? "false"), StringComparer.Ordinal);
            var bad = expect.Keys.Where(k => !spec.Visible.Any(r => r.Id == k && !r.ByCode)).ToList();
            if (unknown.Count > 0 || bad.Count > 0 || expect.Count == 0)
            {
                throw new InvalidOperationException($"{FixtureFile}: fixture {id}: unknown entries [{string.Join(", ", unknown)}], expect keys that are not grader rubrics [{string.Join(", ", bad)}], or no expect");
            }

            fixtures.Add(new GraderFixture(id, m.Get("request") ?? string.Empty, spec, set, expect));
        }

        return fixtures;
    }

    /// <summary>Grades each fixture with the grader agent, the same input as in a run, and compares each verdict with the known answer.</summary>
    public static async Task<IReadOnlyList<FixtureLine>> RunAsync(PackContext pack, AgentGateway gateway, CancellationToken ct)
    {
        var hooks = pack.Pack.Compose!;
        var rules = File.ReadAllText(Path.Combine(pack.Dir, "grading.md"));
        var lines = new List<FixtureLine>();
        foreach (var f in Load(pack))
        {
            var task = new EngineComposeTask(f.Id, f.Spec, pack.Catalog, hooks, string.Empty, rules);
            var rubrics = f.Spec.Visible.Where(r => !r.ByCode).ToList();
            var answer = await gateway.CallAsync(ComposeLoop.GraderAgent, ComposeLoop.GraderInput(task, f.Spec, f.Set, rubrics), ct);
            var verdicts = ComposeLoop.ReadVerdicts(answer, rubrics);
            var wrong = f.Expect.Where(e => verdicts.First(v => v.Rubric == e.Key).Ok != e.Value)
                .Select(e => $"{e.Key}: expected {e.Value}, said {verdicts.First(v => v.Rubric == e.Key).Ok?.ToString() ?? "nothing"}").ToList();
            lines.Add(new FixtureLine(f.Id, f.Set, f.Expect.Count - wrong.Count, f.Expect.Count, wrong, answer.Error));
        }

        return lines;
    }
}
