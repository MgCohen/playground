using PatternEngine.Domains.Topology;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Compose.Domain;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Plugins.Agents;
using PatternEngine.Plugins.Settings;
using PatternEngine.Tests.Support;

namespace PatternEngine.Tests.Topology;

/// <summary>The grader fixtures of topology (G2) and the stamp of the grader files (B8). No model.</summary>
public sealed class GraderCheckTests
{
    private static readonly string Dir = Repo.Path("src", "PatternEngine", "Domains", "Topology");
    private static readonly PackContext Pack = new(new TopologyPack(), Dir, Settings.Load(Repo.Path("pe.settings.yaml"), Path.Combine(Dir, "pack.settings.yaml")), PatternCatalog.Load(Path.Combine(Dir, "patterns")));

    [Test]
    public void Each_fixture_loads_with_known_entries_and_grader_rubrics()
    {
        var fixtures = GraderCheck.Load(Pack);
        Assert.That(fixtures, Has.Count.GreaterThanOrEqualTo(6));
        Assert.That(fixtures.SelectMany(f => f.Expect.Values).Distinct().Count(), Is.EqualTo(2), "the fixtures have true and false answers");
    }

    [Test]
    public async Task A_grader_that_says_yes_to_all_fails_each_false_fixture()
    {
        var judge = new ScriptedJudge(call => TopologyTests.Grade(call));
        var lines = await GraderCheck.RunAsync(Pack, new AgentGateway(judge, Path.GetTempPath(), new RunLimits(80, 1.0, 60)), CancellationToken.None);
        var falses = GraderCheck.Load(Pack).Sum(f => f.Expect.Values.Count(v => !v));
        Assert.That(lines.Sum(l => l.Total - l.Right), Is.EqualTo(falses));
        Assert.That(judge.Calls.Select(c => c.Input), Has.All.Not.Contain("- io:"), "the grader never gets a code rubric");
    }

    [Test]
    public void The_stamp_is_old_with_no_file_another_model_or_a_changed_grader_file()
    {
        var results = Path.Combine(Path.GetTempPath(), "pe-tests", Guid.NewGuid().ToString("N")[..8]);
        var files = new Dictionary<string, string> { ["agents/grader.md"] = "aaa", ["grading.md"] = "bbb" };
        Assert.That(GraderStamp.Old(results, "topology", "haiku", files), Does.StartWith("no grader fixture run"));
        new GraderStamp("topology", "haiku", files, 8, 8, "now").Write(results);
        Assert.That(GraderStamp.Old(results, "topology", "haiku", files), Is.Null);
        Assert.That(GraderStamp.Old(results, "topology", "sonnet", files), Does.Contain("not sonnet"));
        Assert.That(GraderStamp.Old(results, "topology", "haiku", new Dictionary<string, string>(files) { ["grading.md"] = "ccc" }), Does.Contain("grading.md"));
    }
}
