using System.Text.Json.Nodes;
using PatternEngine.Domains.Shapes;
using PatternEngine.Features.Cases.Domain;
using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Catalog.Domain;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.GraderTrust.Domain;
using PatternEngine.Tests.Support;

namespace PatternEngine.Tests.Parity;

/// <summary>
/// The C# engine must give the same results as the Node spike on the same shapes data.
/// The goldens come from spike/tools/export-parity.mjs. No model runs.
/// </summary>
public sealed class ShapesParityTests
{
    private static readonly PatternCatalog Catalog = PatternCatalog.Load(Repo.Parity("shapes", "patterns"));
    private static readonly ShapesPack Pack = new();
    private static readonly RequestTemplates Templates = new("Draw {text}.", "Draw {group} with, from left to right: {list}.");

    [Test]
    public void Validator_gives_the_same_errors_and_warnings()
    {
        var golden = Repo.Json(Repo.Parity("validate.json"))!;
        var findings = CatalogRules.Validate(Catalog);
        Assert.That(findings.Where(f => f.Severity == Severity.Error), Is.Empty);
        var warnings = findings.Where(f => f.Severity == Severity.Warning).Select(f => $"{f.Where}: {f.Message}");
        Assert.That(warnings, Is.EquivalentTo(golden["warnings"]!.AsArray().Select(w => w!.GetValue<string>())));
    }

    [Test]
    public void Index_is_the_same()
    {
        var golden = File.ReadAllText(Repo.Parity("index.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.That(CatalogViews.IndexMarkdown(Catalog, "shapes"), Is.EqualTo(golden));
    }

    [Test]
    public void Graph_is_the_same()
    {
        var golden = Repo.Json(Repo.Parity("graph.json"))!;
        var graph = CatalogViews.Graph(Catalog);
        Assert.That(graph.Nodes.Count, Is.EqualTo(golden["nodes"]!.AsArray().Count));
        var edges = graph.Edges.Select(e => $"{e.From}>{e.To}:{e.Type}:{e.Count}:{e.Only}");
        var want = golden["edges"]!.AsArray().Select(e => $"{e!["from"]}>{e["to"]}:{e["type"]}:{e["count"]}:{e["only"]}");
        Assert.That(edges, Is.EqualTo(want));
    }

    [TestCase(15, 7, 4, "cases-seed7.json")]
    [TestCase(60, 42, 6, "cases-seed42.json")]
    public void Generator_gives_the_same_cases_for_the_same_seed(int count, int seed, int maxChildren, string golden)
    {
        var cases = GeneratorSource.Generate(Catalog, new GeneratorSettings(count, seed, maxChildren, 7, Templates));
        var want = Repo.Json(Repo.Parity(golden))!.AsArray();
        Assert.That(cases, Has.Count.EqualTo(want.Count));
        for (var i = 0; i < want.Count; i++)
        {
            var w = want[i]!;
            Assert.That(cases[i].Id, Is.EqualTo(w["id"]!.GetValue<string>()));
            Assert.That(cases[i].Request, Is.EqualTo(w["request"]!.GetValue<string>()), cases[i].Id);
            Assert.That(cases[i].Tags, Is.EqualTo(w["tags"]!.AsArray().Select(t => t!.GetValue<string>())), cases[i].Id);
            Assert.That(JsonNode.DeepEquals(Repo.ToJson(cases[i].Expect.Tree), w["expect"]!["tree"]), Is.True, cases[i].Id);
        }
    }

    [Test]
    public void References_are_the_same_text()
    {
        var want = Repo.Json(Repo.Parity("references.json"))!.AsObject();
        foreach (var c in AllCases().Where(c => c.Expect.Outcome == Outcomes.Match))
        {
            Assert.That(ShapesReference.Svg(c.Expect.Tree!), Is.EqualTo(want[c.Id]!.GetValue<string>()), c.Id);
        }
    }

    [Test]
    public void Grader_trust_gives_the_same_counts()
    {
        var golden = Repo.Json(Repo.Parity("trust.json"))!;
        var context = new TrustContext(Pack, Repo.Parity("shapes"), Catalog, AllCases(), new TrustSettings(1, 1, 1, null));
        var references = new ReferenceCheck().Run(context);
        Assert.That(references.Counts["references"], Is.EqualTo(golden["references"]!["n"]!.GetValue<int>()));
        Assert.That(references.Counts["pass"], Is.EqualTo(golden["references"]!["pass"]!.GetValue<int>()));

        var mutations = new MutationCheck().Run(context);
        Assert.That(mutations.Counts["applied"], Is.EqualTo(golden["total"]!["applied"]!.GetValue<int>()));
        Assert.That(mutations.Counts["caught"], Is.EqualTo(golden["total"]!["caught"]!.GetValue<int>()));
        Assert.That(mutations.Counts["right"], Is.EqualTo(golden["total"]!["right"]!.GetValue<int>()));
        Assert.That(mutations.Ok, Is.True);
    }

    [Test]
    public void Fixtures_give_the_expected_verdicts()
    {
        var context = new TrustContext(Pack, Repo.Parity("shapes"), Catalog, [], new TrustSettings(1, 1, 1, null));
        var result = new FixtureCheck().Run(context);
        Assert.That(result.Lines.Where(l => !l.Ok).Select(l => l.Text), Is.Empty);
        Assert.That(result.Lines, Has.Count.EqualTo(16));
    }

    [Test]
    public void Regrade_of_stored_outputs_gives_the_same_verdicts()
    {
        var golden = Repo.Json(Repo.Parity("regrade.json"))!.AsArray();
        Assert.That(golden, Has.Count.GreaterThan(100));
        foreach (var item in golden)
        {
            var file = item!["file"]!.GetValue<string>();
            var expect = Expect.From(PatternEngine.Plugins.Yaml.Yaml.Parse(item["expect"]!.ToJsonString()));
            var failed = OutputGrading.Failed(File.ReadAllText(Repo.Parity("outputs", file)), expect, Pack, Catalog);
            var want = item["failed"]!.AsObject().ToDictionary(e => e.Key, e => e.Value!.GetValue<string>());
            Assert.That(failed, Is.EquivalentTo(want), file);
        }
    }

    private static IReadOnlyList<Case> AllCases()
    {
        var hand = new HandSource().Load(new CaseContext(Repo.Parity("shapes"), Catalog, new GeneratorSettings(0, 7, 4, 7, Templates)));
        var seen = new HashSet<string>();
        return GeneratorSource.Generate(Catalog, new GeneratorSettings(15, 7, 4, 7, Templates)).Concat(hand)
            .Concat(GeneratorSource.Generate(Catalog, new GeneratorSettings(60, 42, 6, 7, Templates))).Concat(hand)
            .Where(c => seen.Add(c.Id)).ToList();
    }
}
