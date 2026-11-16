using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Features.Eval.Domain;
using PatternEngine.Features.Grading.Domain;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Plugins.Agents;
using PatternEngine.Tests.Support;

namespace PatternEngine.Tests.Engine;

/// <summary>The engine loop with a scripted judge: no model, no cost. Each test checks the trace and the graders.</summary>
public sealed class EngineTests
{
    [Test]
    public async Task One_object_is_decided_composed_and_graded()
    {
        var judge = ShapesScript.Judge();
        var run = await ShapesScript.RunAsync("Draw a house with a chimney and a red door.", judge);
        Assert.That(run.Trace.Outcome, Is.EqualTo(Outcomes.Match));
        Assert.That(run.Trace.Tree!.ToString(), Is.EqualTo("house(chimney){door_fill:red}"));
        Assert.That(run.Stats.Calls, Is.EqualTo(2));
        var grades = Grade(run, new Expect { Outcome = Outcomes.Match, Tree = run.Trace.Tree });
        Assert.That(grades.Verdicts.Where(v => !v.Value.Ok).Select(v => $"{v.Key}: {v.Value.Failures[0]}"), Is.Empty);
        Assert.That(grades.Consistent, Is.True);
        Assert.That(run.Trace.Hashes!.Patterns.Keys, Is.EquivalentTo(new[] { "house" }));
    }

    [Test]
    public async Task A_group_makes_child_nodes_and_code_assembles_it()
    {
        var run = await ShapesScript.RunAsync("Draw a street with, from left to right: a tree; a shed with a blue door; and a tower.", ShapesScript.Judge());
        Assert.That(run.Trace.Tree!.ToString(), Is.EqualTo("street(row)[tree(plain), shed(plain){door_fill:blue}, tower(plain)]"));
        Assert.That(run.Trace.Graph, Has.Count.EqualTo(4));
        var grades = Grade(run, new Expect { Outcome = Outcomes.Match, Tree = run.Trace.Tree });
        Assert.That(grades.Pass, Is.True, string.Join("; ", grades.Verdicts.Where(v => !v.Value.Ok).Select(v => v.Value.Failures[0])));
    }

    [Test]
    public async Task A_failed_check_gets_a_retry_with_the_failures()
    {
        var composes = 0;
        var judge = ShapesScript.Judge(call =>
            call.Agent == "composer" && Interlocked.Increment(ref composes) == 1 ? new { fragment = "<g data-p=\"house\" data-v=\"plain\"></g>" } : null);
        var run = await ShapesScript.RunAsync("Draw a house.", judge);
        Assert.That(run.Trace.Attempts, Is.EqualTo(2));
        Assert.That(judge.Calls.Last(c => c.Agent == "composer").Input, Does.Contain("The last attempt failed these checks"));
        Assert.That(run.Stats.Error, Is.Null);
    }

    [Test]
    public async Task A_run_limit_stops_the_run_with_a_stop_reason()
    {
        var run = await ShapesScript.RunAsync("Draw a house.", ShapesScript.Judge(), s => s with { Run = s.Run with { AgentCalls = 1 } });
        Assert.That(run.Trace.StopReason, Does.Contain("1 agent calls"));
        Assert.That(run.Stats.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task A_node_deeper_than_the_limit_is_unresolved()
    {
        var run = await ShapesScript.RunAsync("Draw a street with, from left to right: a tree; and a house.", ShapesScript.Judge(),
            s => s.WithoutEscalation() with { Node = s.Node with { MaxDepth = 0 } });
        Assert.That(run.Trace.Graph.Where(n => n.Depth == 1).Select(n => n.Reason), Is.All.EqualTo("too deep"));
        Assert.That(run.Trace.Tree!.Parts, Is.Null);
    }

    [Test]
    public async Task A_pattern_that_is_not_allowed_is_not_used()
    {
        var judge = ShapesScript.Judge(call => call.Agent == "decider" ? new { status = "solved", pattern = "door", reason = "a leaf" } : null);
        var run = await ShapesScript.RunAsync("Draw a door.", judge, s => s.WithoutEscalation());
        Assert.That(run.Trace.Outcome, Is.EqualTo(Outcomes.Stopped));
        Assert.That(run.Trace.Graph[0].Reason, Does.Contain("not allowed"));
    }

    [Test]
    public async Task No_fit_runs_the_author_agent_and_uses_the_proposed_pattern()
    {
        var judge = ShapesScript.Judge(call => call.Agent switch
        {
            "decider" => new { status = "none", pattern = "tower", nearest = "tower", new_id = "Light House", deltas = new[] { "add a lamp room" }, reason = "no pattern has a light" },
            "pattern-author" => new
            {
                kind = "object",
                phrase = "a lighthouse",
                use_when = new[] { "The request names a lighthouse." },
                parts = new[] { new { @ref = "body", count = "1" }, new { @ref = "roof", count = "1" }, new { @ref = "door", count = "1" }, new { @ref = "window", count = "0..6" } },
                intent = "A tower with a light on top.",
                rules = new[] { "Do: make the body tall." },
            },
            _ => null,
        });
        var run = await ShapesScript.RunAsync("Draw a lighthouse.", judge);
        Assert.That(run.Trace.Outcome, Is.EqualTo(Outcomes.NoFit));
        Assert.That(run.Trace.Proposed, Is.EqualTo("patterns/light-house.md"));
        var proposed = run.Catalog.Get("light-house")!;
        Assert.That(proposed.Status, Is.EqualTo(Features.Catalog.Domain.Shared.PatternStatus.Proposed));
        Assert.That(proposed.Related, Does.Contain("tower"));
        var grades = Grade(run, new Expect { Outcome = Outcomes.NoFit, Nearest = ["tower"] });
        Assert.That(grades.Verdicts.Where(v => !v.Value.Ok).Select(v => $"{v.Key}: {v.Value.Failures[0]}"), Is.Empty);
    }

    [Test]
    public async Task A_bad_author_draft_falls_back_to_the_code_scaffold()
    {
        var judge = ShapesScript.Judge(call => call.Agent switch
        {
            "decider" => new { status = "none", pattern = "tower", nearest = "tower", new_id = "windmill", deltas = new[] { "add sails" }, reason = "sails" },
            "pattern-author" => new { phrase = "a windmill", use_when = new[] { "x" }, parts = new[] { new { @ref = "sail", count = "4" } }, intent = "x", rules = new[] { "x" } },
            _ => null,
        });
        var run = await ShapesScript.RunAsync("Draw a windmill.", judge);
        Assert.That(run.Trace.Notes, Has.Some.Contains("code wrote a scaffold"));
        Assert.That(run.Catalog.Get("windmill")!.Parts.Select(p => p.Ref), Does.Contain("body"));
        Assert.That(judge.Calls.Count(c => c.Agent == "pattern-author"), Is.EqualTo(2));
    }

    [Test]
    public async Task The_trace_tree_matches_the_output_tree()
    {
        var run = await ShapesScript.RunAsync("Draw a street with, from left to right: a house with a chimney; and a tree.", ShapesScript.Judge());
        var grades = Grade(run, new Expect { Outcome = Outcomes.Match, Tree = run.Trace.Tree });
        Assert.That(grades.Consistent, Is.True);
    }

    internal static GradeReport Grade(EngineRunResult run, Expect expect, Dictionary<string, string>? hidden = null)
    {
        var c = new Case { Id = "t", Source = "test", Tags = [], Request = "r", Expect = expect, Hidden = hidden };
        var output = File.Exists(run.OutputFile) ? File.ReadAllText(run.OutputFile) : null;
        return RunGrading.Grade(c, new Domains.Shapes.ShapesPack(), run.Catalog, run.Trace, output, GraderSet.Default.Select(["selection", "built", "structure", "rules", "proposed", "questions"], new Domains.Shapes.ShapesPack()));
    }
}
