using PatternEngine.Domains.Blocks;
using PatternEngine.Domains.Topology;
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

namespace PatternEngine.Tests.Topology;

/// <summary>The third domain: design patterns with typed ports, a code-assembled system, and a flow grader.</summary>
public sealed class TopologyTests
{
    private static readonly string Dir = Repo.Path("src", "PatternEngine", "Domains", "Topology");
    private static readonly PatternCatalog Catalog = PatternCatalog.Load(Path.Combine(Dir, "patterns"));
    private static readonly TopologyPack Pack = new();

    [Test]
    public void The_catalog_has_no_errors()
    {
        Assert.That(CatalogRules.Validate(Catalog).Where(f => f.Severity == Severity.Error), Is.Empty);
    }

    [Test]
    public void A_system_sends_the_events_of_the_pipeline_to_the_observer()
    {
        var tree = new TreeNode { P = "cqrs_system", V = "plain", Parts = [new() { P = "command_pipeline", V = "validated" }, new() { P = "observer" }] };
        var parsed = Pack.Adapter.Parse(new TopologyReference().Render(tree, Catalog)!);
        var top = (BlockNode)parsed.Roots[0];
        Assert.That(top.Wires, Does.Contain(("c1.event", "c2.event")));
        Assert.That(StructureGrader.Check(parsed, Catalog, Pack.Adapter).Failures, Is.Empty);
        Assert.That(FlowGrader.Flow(top, Catalog), Is.Empty);
    }

    [Test]
    public void A_wire_between_two_types_fails()
    {
        var parsed = Pack.Adapter.Parse("""{ "p": "query_handler", "v": "plain", "parts": [ { "id": "in", "p": "query_endpoint" }, { "id": "read", "p": "reader" } ], "wires": [ ["request", "in.request"], ["in.query", "read.query"], ["in.query", "view"] ] }""");
        Assert.That(StructureGrader.Check(parsed, Catalog, Pack.Adapter).Failures, Has.Some.Contains("joins a Query to a View"));
    }

    [Test]
    public void A_part_that_never_runs_fails_the_flow()
    {
        var parsed = Pack.Adapter.Parse("""{ "p": "new_block", "v": "plain", "inputs": ["event"], "outputs": ["effect"], "parts": [ { "id": "s1", "p": "subscriber" }, { "id": "s2", "p": "subscriber" } ], "wires": [ ["event", "s1.event"], ["s1.effect", "effect"], ["s2.effect", "s2.event"] ] }""");
        var v = new FlowGrader().Grade(new Features.Grading.Domain.Shared.GradeInput
        {
            Case = new Case { Id = "x", Source = "t", Tags = [], Request = "r", Expect = new Expect { Outcome = Outcomes.NoFit } },
            Catalog = Catalog,
            Pack = Pack,
            Parsed = parsed,
        });
        Assert.That(v.Ok, Is.False);
    }

    [Test]
    public async Task A_remainder_that_no_pattern_fits_is_authored_and_the_request_stays_a_match()
    {
        var settings = Settings.Load(Repo.Path("pe.settings.yaml"), Path.Combine(Dir, "pack.settings.yaml"));
        var pack = new PackContext(Pack, Dir, settings, Catalog);
        var judge = new ScriptedJudge(call =>
        {
            var request = call.Input.Split('\n')[0];
            return call.Agent switch
            {
                // The remainder: the decider says "outside", and code reads it as a no-fit, because a remainder is in scope.
                "decider" when request.Contains("delay", StringComparison.Ordinal) && call.Input.Contains("remainder of a partial fit", StringComparison.Ordinal)
                    => new { status = "outside", pattern = "observer", reason = "no pattern waits" },
                "decider" => new { status = "partial_fit", pattern = "command_pipeline", variant = "plain", open = new[] { "wait a delay between the tries" }, reason = "the pipeline runs the payment" },
                "pattern-author" => new
                {
                    kind = "object",
                    phrase = "a delay",
                    use_when = new[] { "The request waits a time before the next step." },
                    parts = new[] { new { @ref = "handler", count = "1" } },
                    intent = "Waits, then sends the command again.",
                    rules = new[] { "Do: wait before the next try." },
                },
                "composer" => new { fragment = TopologyReference.Block("command_pipeline", "plain")!.ToJsonString() },
                _ => null,
            };
        });
        var dir = Path.Combine(Path.GetTempPath(), "pe-tests", Guid.NewGuid().ToString("N")[..8]);
        EngineRun.Prepare(pack, dir);
        var run = await EngineRun.RunAsync(pack, "Design a part that tries a failed payment again three times, with a delay between the tries.", dir, EngineSettings.From(settings), judge, new NoUser(), CancellationToken.None);

        Assert.That(run.Trace.Outcome, Is.EqualTo(Outcomes.Match));
        Assert.That(run.Trace.Remainder.Select(r => r.Outcome), Is.EqualTo(new[] { Outcomes.NoFit }));
        Assert.That(run.Trace.Remainder[0].Proposed, Is.Not.Null);
        Assert.That(File.Exists(Path.Combine(dir, run.Trace.Remainder[0].Proposed!)), Is.True);
        Assert.That(judge.Calls.Any(c => c.Agent == "escalation"), Is.False);
        Assert.That(run.Trace.Questions, Is.Empty);
        var expect = new Expect { Outcome = Outcomes.Match, Tree = new TreeNode { P = "command_pipeline" }, Remainder = [Outcomes.NoFit] };
        Assert.That(SelectionGrader.Check(run.Trace, expect).Failures, Is.Empty);
        Assert.That(SelectionGrader.Check(run.Trace, expect with { Remainder = [Outcomes.Match] }).Failures, Has.Some.Contains("expected remainder"));
    }

    [Test]
    public async Task The_engine_decides_a_system_and_code_assembles_it()
    {
        var settings = Settings.Load(Repo.Path("pe.settings.yaml"), Path.Combine(Dir, "pack.settings.yaml"));
        var pack = new PackContext(Pack, Dir, settings, Catalog);
        var judge = new ScriptedJudge(call =>
        {
            var request = call.Input.Split('\n')[0];
            return call.Agent switch
            {
                "decider" when request.Contains("system", StringComparison.Ordinal) => new { status = "solved", pattern = "cqrs_system", variant = "plain", children = new[] { "users place orders", "an email for each order" }, reason = "two jobs" },
                "decider" when request.Contains("orders", StringComparison.Ordinal) => new { status = "solved", pattern = "command_pipeline", variant = "plain", reason = "changes data" },
                "decider" => new { status = "solved", pattern = "observer", variant = "plain", reason = "reacts" },
                "composer" => new { fragment = TopologyReference.Block(call.Input.Contains("Pattern: observer", StringComparison.Ordinal) ? "observer" : "command_pipeline", "plain")!.ToJsonString() },
                _ => null,
            };
        });
        var dir = Path.Combine(Path.GetTempPath(), "pe-tests", Guid.NewGuid().ToString("N")[..8]);
        EngineRun.Prepare(pack, dir);
        var run = await EngineRun.RunAsync(pack, "Design a system where users place orders and get an email for each order.", dir, EngineSettings.From(settings), judge, new NoUser(), CancellationToken.None);
        var expect = new Expect { Outcome = Outcomes.Match, Tree = new TreeNode { P = "cqrs_system", Parts = [new() { P = "command_pipeline" }, new() { P = "observer" }] } };
        var c = new Case { Id = "s", Source = "t", Tags = [], Request = "r", Expect = expect };
        var grades = RunGrading.Grade(c, Pack, run.Catalog, run.Trace, File.ReadAllText(run.OutputFile), GraderSet.Default.Select(settings.Section("grading").List("graders"), Pack));
        Assert.That(grades.Verdicts.Where(v => !v.Value.Ok).Select(v => $"{v.Key}: {v.Value.Failures[0]}"), Is.Empty);
        Assert.That(grades.Consistent, Is.True);
    }
}
