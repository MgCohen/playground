using PatternEngine.Domains.Blocks;
using PatternEngine.Domains.Topology;
using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Catalog.Domain;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Features.Engine.Domain.Shared;
using PatternEngine.Features.Grading.Domain;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Features.Packs.Domain.Shared;
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
    public void A_retry_is_a_loop_with_a_merge()
    {
        // The handler takes the first command from the endpoint and the next tries from the retrier: a merge and a loop.
        var retry = (BlockNode)Pack.Adapter.Parse("""{ "p": "retry_pipeline", "v": "plain", "inputs": ["request"], "outputs": ["event"], "parts": [ { "id": "in", "p": "endpoint" }, { "id": "run", "p": "handler" }, { "id": "again", "p": "retrier" } ], "wires": [ ["request", "in.request"], ["in.command", "run.command"], ["run.event", "again.event"], ["again.command", "run.command"], ["run.event", "event"] ] }""").Roots[0];
        Assert.That(BlockAdapter.HasLoop(retry), Is.True);
        Assert.That(BlockAdapter.Wiring(retry, Catalog, true, merge: true), Is.Empty);
        Assert.That(FlowGrader.Flow(retry, Catalog), Is.Empty);
        Assert.That(FlowGrader.Io(retry, Catalog, new IoSpec { Inputs = ["Request"], Outputs = ["Event"] }), Is.Empty);
    }

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
                    parts = new[] { new { @ref = "subscriber", count = "1" } },
                    inputs = new[] { new { name = "event", type = "Event" } },
                    outputs = new[] { new { name = "effect", type = "Effect" } },
                    intent = "Waits after a failed try.",
                    rules = new[] { "Do: wait before the next try." },
                },
                "composer" when call.Input.Contains("Pattern: command_pipeline", StringComparison.Ordinal) => new { fragment = TopologyReference.Block("command_pipeline", "plain")!.ToJsonString() },
                "composer" => new { fragment = """{ "p": "PROPOSED", "v": "plain", "parts": [ { "id": "s", "p": "subscriber" } ], "wires": [ ["event", "s.event"], ["s.effect", "effect"] ] }""".Replace("PROPOSED", ProposedId(call.Input), StringComparison.Ordinal) },
                _ => null,
            };
        });
        var dir = Path.Combine(Path.GetTempPath(), "pe-tests", Guid.NewGuid().ToString("N")[..8]);
        EngineRun.Prepare(pack, dir);
        var run = await EngineRun.RunAsync(pack, "Design a part that tries a failed payment again three times, with a delay between the tries.", dir, EngineSettings.From(settings), judge, new NoUser(), CancellationToken.None);

        Assert.That(run.Trace.Outcome, Is.EqualTo(Outcomes.Match));
        Assert.That(run.Trace.Remainder.Select(r => r.Outcome), Is.EqualTo(new[] { Outcomes.NoFit }));
        Assert.That(run.Trace.Remainder[0].Joined, Is.True);
        Assert.That(File.Exists(Path.Combine(dir, run.Trace.Remainder[0].Proposed!)), Is.True);
        Assert.That(judge.Calls.Any(c => c.Agent is "escalation" or "joiner"), Is.False);
        Assert.That(run.Trace.Questions, Is.Empty);

        // The solution: the pipeline and the authored delay, joined Event to Event by code.
        var authored = run.Trace.Remainder[0].Pattern!;
        var expect = new Expect { Outcome = Outcomes.Match, Tree = new TreeNode { P = Solution.Id, Parts = [new() { P = "command_pipeline" }, new() { P = authored }] }, Remainder = [Outcomes.NoFit], Io = new IoSpec { Inputs = ["Request"], Outputs = ["Effect"] } };
        var grades = RunGrading.Grade(new Case { Id = "r", Source = "t", Tags = [], Request = "r", Expect = expect }, Pack, run.Catalog, run.Trace, File.ReadAllText(run.OutputFile), GraderSet.Default.Plan(settings.Section("grading"), Pack));
        Assert.That(grades.Verdicts.Where(v => !v.Value.Ok).Select(v => $"{v.Key}: {v.Value.Failures[0]}"), Is.Empty);
        var top = (BlockNode)Pack.Adapter.Parse(File.ReadAllText(run.OutputFile)).Roots[0];
        Assert.That(top.Wires, Does.Contain(("s1.event", "s2.event")));
        Assert.That(SelectionGrader.Check(run.Trace, expect with { Remainder = [Outcomes.Match] }).Failures, Has.Some.Contains("expected remainder"));
        Assert.That(grades.SolutionScore, Is.EqualTo(1));

        // The output is the gate: a solution that gives no Saved output fails, whatever its tree.
        var saved = RunGrading.Grade(new Case { Id = "r", Source = "t", Tags = [], Request = "r", Expect = expect with { Io = new IoSpec { Inputs = ["Request"], Outputs = ["Saved"] } } }, Pack, run.Catalog, run.Trace, File.ReadAllText(run.OutputFile), GraderSet.Default.Plan(settings.Section("grading"), Pack));
        Assert.That(saved.Pass, Is.False);
        Assert.That(saved.Verdicts["flow"].Failures, Has.Some.Contains("no Saved output"));
    }

    [Test]
    public void A_solution_with_a_pattern_that_has_no_wire_fails_the_flow()
    {
        var top = (BlockNode)Pack.Adapter.Parse("""{ "p": "solution", "v": "plain", "inputs": ["s1_request", "s2_request"], "outputs": ["s1_view", "s2_view"], "parts": [ { "id": "s1", "p": "query_handler", "v": "plain" }, { "id": "s2", "p": "query_handler", "v": "plain" } ], "wires": [ ["s1_request", "s1.request"], ["s2_request", "s2.request"], ["s1.view", "s1_view"], ["s2.view", "s2_view"] ] }""").Roots[0];
        Assert.That(FlowGrader.Islands(top), Has.Count.EqualTo(2));
    }

    private static string ProposedId(string input) =>
        System.Text.RegularExpressions.Regex.Match(input, "new pattern \"([a-z0-9-]+)\"").Groups[1].Value;

    [Test]
    public void Code_joins_a_port_when_one_type_matches_and_asks_when_more_match()
    {
        SolutionItem Item(string id, string p) => new(id, p, "{}");
        var (plan, choices) = JoinStep.Plan(Catalog, [Item("s1", "command_pipeline"), Item("s2", "observer")]);
        Assert.That(plan.Wires, Is.EqualTo(new[] { new JoinWire("s1.event", "s2.event") }));
        Assert.That(plan.Inputs, Is.EqualTo(new[] { "s1_request" }));
        Assert.That(plan.Outputs, Is.EqualTo(new[] { "s1_event", "s2_effect" }));
        Assert.That(choices, Is.Empty);

        (_, choices) = JoinStep.Plan(Catalog, [Item("s1", "command_pipeline"), Item("s2", "command_pipeline"), Item("s3", "observer")]);
        Assert.That(choices.Single().Options, Is.EqualTo(new[] { "s1.event", "s2.event" }));
    }

    [Test]
    public void An_assembled_system_joins_by_the_ports_of_its_fragment()
    {
        var system = new TopologyReference().Render(new TreeNode { P = "cqrs_system", V = "plain", Parts = [new() { P = "command_pipeline", V = "plain" }, new() { P = "observer" }] }, Catalog)!;
        var items = new[] { new SolutionItem("s1", "cqrs_system", system), new SolutionItem("s2", "observer", TopologyReference.Block("observer", "plain")!.ToJsonString()) };
        var (plan, choices) = JoinStep.Plan(Catalog, items, Pack.Layout);
        Assert.That(choices, Is.Empty);
        Assert.That(plan.Wires, Does.Contain(new JoinWire("s1.c1_event", "s2.event")));
        var top = (BlockNode)Pack.Adapter.Parse(Pack.Layout.Join(items, plan)!).Roots[0];
        Assert.That(BlockAdapter.Wiring(top, Catalog, true), Is.Empty);
        Assert.That(FlowGrader.Flow(top, Catalog), Is.Empty);
    }

    [Test]
    public void A_joined_solution_passes_the_wiring_and_flow_checks()
    {
        var items = new[]
        {
            new SolutionItem("s1", "command_pipeline", TopologyReference.Block("command_pipeline", "plain")!.ToJsonString()),
            new SolutionItem("s2", "observer", TopologyReference.Block("observer", "plain")!.ToJsonString()),
        };
        var (plan, _) = JoinStep.Plan(Catalog, items);
        var parsed = Pack.Adapter.Parse(Pack.Layout.Join(items, plan)!);
        Assert.That(StructureGrader.Check(parsed, Catalog, Pack.Adapter).Failures, Is.Empty);
        var top = (BlockNode)parsed.Roots[0];
        Assert.That(BlockAdapter.Wiring(top, Catalog, true), Is.Empty);
        Assert.That(FlowGrader.Flow(top, Catalog), Is.Empty);
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
        var grades = RunGrading.Grade(c, Pack, run.Catalog, run.Trace, File.ReadAllText(run.OutputFile), GraderSet.Default.Plan(settings.Section("grading"), Pack));
        Assert.That(grades.Verdicts.Where(v => !v.Value.Ok).Select(v => $"{v.Key}: {v.Value.Failures[0]}"), Is.Empty);
        Assert.That(grades.Consistent, Is.True);
    }
}
