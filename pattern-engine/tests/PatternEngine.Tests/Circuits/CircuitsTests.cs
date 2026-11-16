using PatternEngine.Domains.Blocks;
using PatternEngine.Domains.Circuits;
using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Catalog.Domain;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Features.Engine.Domain.Shared;
using PatternEngine.Features.Grading.Domain;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Plugins.Agents;
using PatternEngine.Plugins.Settings;
using PatternEngine.Tests.Support;

namespace PatternEngine.Tests.Circuits;

/// <summary>The second domain: the same core, a pack with typed ports and a simulator.</summary>
public sealed class CircuitsTests
{
    private static readonly string Dir = Repo.Path("src", "PatternEngine", "Domains", "Circuits");
    private static readonly PatternCatalog Catalog = PatternCatalog.Load(Path.Combine(Dir, "patterns"));
    private static readonly CircuitsPack Pack = new();

    [Test]
    public void A_latch_is_a_loop_that_settles()
    {
        // An SR latch: q = not(r or qn), qn = not(s or q).
        var latch = (BlockNode)Pack.Adapter.Parse("""{ "p": "latch", "v": "plain", "inputs": ["s", "r"], "outputs": ["q"], "parts": [ { "id": "o1", "p": "or" }, { "id": "n1", "p": "not" }, { "id": "o2", "p": "or" }, { "id": "n2", "p": "not" } ], "wires": [ ["r", "o1.a"], ["n2.y", "o1.b"], ["o1.y", "n1.a"], ["s", "o2.a"], ["n1.y", "o2.b"], ["o2.y", "n2.a"], ["n1.y", "q"] ] }""").Roots[0];
        Assert.That(BlockAdapter.Wiring(latch, Catalog), Is.Empty);
        Assert.That(BlockAdapter.HasLoop(latch), Is.True);
        Assert.That(Simulator.Run(latch, Catalog, new Dictionary<string, bool> { ["s"] = true, ["r"] = false })["q"], Is.True);
        Assert.That(Simulator.Run(latch, Catalog, new Dictionary<string, bool> { ["s"] = false, ["r"] = true })["q"], Is.False);
    }

    [Test]
    public void A_loop_that_never_settles_fails()
    {
        var ring = (BlockNode)Pack.Adapter.Parse("""{ "p": "ring", "v": "plain", "inputs": [], "outputs": ["y"], "parts": [ { "id": "n1", "p": "not" } ], "wires": [ ["n1.y", "n1.a"], ["n1.y", "y"] ] }""").Roots[0];
        Assert.That(() => Simulator.Run(ring, Catalog, new Dictionary<string, bool>()), Throws.InvalidOperationException.With.Message.Contains("does not settle"));
    }

    [Test]
    public void The_catalog_has_no_errors()
    {
        Assert.That(CatalogRules.Validate(Catalog).Where(f => f.Severity == Severity.Error), Is.Empty);
    }

    [TestCase("half_adder")]
    [TestCase("full_adder")]
    [TestCase("mux2")]
    [TestCase("equal")]
    public void Each_reference_circuit_has_the_right_truth_table(string id)
    {
        var parsed = Pack.Adapter.Parse(new CircuitsReference().Render(new TreeNode { P = id }, Catalog)!);
        Assert.That(BehaviorSpec.All[id].Check((BlockNode)parsed.Roots[0], Catalog), Is.Empty);
    }

    [Test]
    public void The_simulator_runs_nested_blocks()
    {
        var parsed = Pack.Adapter.Parse(new CircuitsReference().Render(new TreeNode { P = "full_adder" }, Catalog)!);
        var outputs = Simulator.Run((BlockNode)parsed.Roots[0], Catalog, new Dictionary<string, bool> { ["a"] = true, ["b"] = true, ["cin"] = true });
        Assert.That(outputs["sum"], Is.True);
        Assert.That(outputs["cout"], Is.True);
    }

    [Test]
    public void An_open_input_fails_the_wiring_check()
    {
        var parsed = Pack.Adapter.Parse("""{ "p": "equal", "v": "plain", "parts": [ { "id": "x1", "p": "xor" }, { "id": "n1", "p": "not" } ], "wires": [ ["a", "x1.a"], ["x1.y", "n1.a"], ["n1.y", "y"] ] }""");
        var v = StructureGrader.Check(parsed, Catalog, Pack.Adapter);
        Assert.That(v.Failures, Has.Some.Contains("\"x1.b\" is not driven"));
    }

    [Test]
    public async Task The_engine_solves_a_circuit_with_a_scripted_judge()
    {
        var settings = Settings.Load(Repo.Path("pe.settings.yaml"), Path.Combine(Dir, "pack.settings.yaml"));
        var pack = new PackContext(Pack, Dir, settings, Catalog);
        var judge = new ScriptedJudge(call => call.Agent switch
        {
            "decider" => new { status = "solved", pattern = "full_adder", variant = "plain", reason = "three bits" },
            "composer" => new { fragment = new CircuitsReference().Render(new TreeNode { P = "full_adder" }, Catalog) },
            _ => null,
        });
        var dir = Path.Combine(Path.GetTempPath(), "pe-tests", Guid.NewGuid().ToString("N")[..8]);
        EngineRun.Prepare(pack, dir);
        var run = await EngineRun.RunAsync(pack, "Build a circuit that adds three bits.", dir, EngineSettings.From(settings), judge, new NoUser(), CancellationToken.None);
        var c = new Case { Id = "c", Source = "t", Tags = [], Request = "r", Expect = new Expect { Outcome = Outcomes.Match, Tree = new TreeNode { P = "full_adder" } } };
        var graders = GraderSet.Default.Plan(settings.Section("grading"), Pack);
        var grades = RunGrading.Grade(c, Pack, run.Catalog, run.Trace, File.ReadAllText(run.OutputFile), graders);
        Assert.That(grades.Verdicts.Keys, Does.Contain("behavior"));
        Assert.That(grades.Verdicts.Where(v => !v.Value.Ok).Select(v => $"{v.Key}: {v.Value.Failures[0]}"), Is.Empty);
    }
}
