using PatternEngine.Domains.Topology;
using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Compose.Domain;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Features.Engine.Domain.Shared;
using PatternEngine.Features.Grading.Domain;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Features.Packs.Domain.Shared;
using PatternEngine.Features.Trace.Domain.Shared;
using PatternEngine.Plugins.Agents;
using PatternEngine.Plugins.Settings;
using PatternEngine.Tests.Support;

namespace PatternEngine.Tests.Topology;

/// <summary>The compose hooks of topology and the compose decider of the engine: spec, code checks, group wrap, ask step. No model.</summary>
public sealed class TopologyComposeTests
{
    private static readonly string Dir = Repo.Path("src", "PatternEngine", "Domains", "Topology");
    private static readonly PatternCatalog Catalog = PatternCatalog.Load(Path.Combine(Dir, "patterns"));
    private static readonly TopologyPack Pack = new();
    private static readonly TopologyCompose Hooks = new();
    private static readonly Settings PackSettings = Settings.Load(Repo.Path("pe.settings.yaml"), Path.Combine(Dir, "pack.settings.yaml"));

    [Test]
    public void The_topology_pack_uses_the_compose_decider_and_the_other_packs_do_not()
    {
        Assert.That(EngineSettings.From(PackSettings).Decider, Is.EqualTo(EngineSettings.ComposeDecider));
        Assert.That(EngineSettings.From(Settings.Load(Repo.Path("pe.settings.yaml"))).Decider, Is.EqualTo(EngineSettings.StatusDecider));
        var settings = ComposeSettings.From(PackSettings);
        Assert.That((settings.CodeChecks, settings.Gaps), Is.EqualTo((true, true)));
    }

    [Test]
    public void An_entry_is_one_variant_and_a_group_is_not_an_entry()
    {
        var ids = EngineComposeTask.Entries(Catalog).Select(e => e.Id).ToList();
        Assert.That(ids, Does.Contain("command_pipeline.plain").And.Contain("command_pipeline.validated").And.Contain("store.plain"));
        Assert.That(ids.Any(i => i.StartsWith("cqrs_system", StringComparison.Ordinal)), Is.False);
        Assert.That(ids.Any(i => i.StartsWith("handler", StringComparison.Ordinal)), Is.False, "a leaf is not an entry");
    }

    [Test]
    public void The_spec_has_a_code_io_rubric_and_one_grader_rubric_for_each_job()
    {
        var (spec, errors) = Hooks.Spec("request", "Save the orders.", [new("input", "request", null), new("output", "Saved", null), new("job", "save the orders", null)], Catalog);
        Assert.That(errors, Is.Empty);
        Assert.That(spec!.Errors, Is.Empty);
        Assert.That(spec.Rubrics.Select(r => (r.Id, r.Check)), Is.EqualTo(new[] { ("io", "code"), ("job-1", "grader") }));
        Assert.That(spec.Rubrics[0].Args["inputs"], Is.EqualTo(new[] { "Request" }), "code writes the type as the catalog has it");
        Assert.That(spec.Level, Is.EqualTo("case"));
    }

    [Test]
    public void A_spec_with_an_unknown_type_or_no_job_is_refused()
    {
        var (spec, errors) = Hooks.Spec("request", "x", [new("input", "Order", null), new("output", "Saved", null)], Catalog);
        Assert.That(spec, Is.Null);
        Assert.That(errors, Has.Some.StartsWith("unknown type \"Order\"").And.Some.EqualTo("no job").And.Some.EqualTo("no input type"));
    }

    [Test]
    public void Io_is_true_for_the_pipeline_and_the_store_and_false_for_the_query_handler_and_the_store()
    {
        Assert.That(TopologyCompose.IoVerdict(Set("command_pipeline.plain", "store.plain"), ["Request"], ["Saved"]).Ok, Is.True);
        var wrong = TopologyCompose.IoVerdict(Set("query_handler.plain", "store.plain"), ["Request"], ["Saved"]);
        Assert.That(wrong.Ok, Is.False);
        Assert.That(wrong.Reason, Does.Contain("needs a Event input"));
    }

    [Test]
    public void A_pattern_with_no_shared_port_type_fails_the_connect_check()
    {
        Assert.That(TopologyCompose.ConnectVerdict(Set("command_pipeline.plain", "observer.plain")).Ok, Is.True);
        var lone = TopologyCompose.ConnectVerdict(Set("command_pipeline.plain", "query_handler.plain"));
        Assert.That(lone.Ok, Is.False);
        Assert.That(lone.Reason, Does.Contain("query_handler.plain"));
    }

    [Test]
    public void A_loop_with_no_entry_fails_and_a_loop_with_an_entry_passes()
    {
        Port P(string t) => new(t.ToLowerInvariant(), t);
        CatalogEntry E(string id, string[] ins, string[] outs) =>
            new(id, Catalog.Get("observer")! with { Id = id, Inputs = ins.Select(P).ToList(), Outputs = outs.Select(P).ToList() }, "plain");
        var closed = new[] { E("a", ["Event"], ["Command"]), E("b", ["Command"], ["Event"]) };
        Assert.That(TopologyCompose.LoopVerdict(closed, ["Request"]).Ok, Is.False);
        var open = new[] { E("a", ["Event", "Request"], ["Command"]), E("b", ["Command"], ["Event"]) };
        Assert.That(TopologyCompose.LoopVerdict(open, ["Request"]).Ok, Is.True);
    }

    [Test]
    public void A_set_that_fits_the_parts_of_a_group_is_wrapped_in_it()
    {
        Assert.That(ComposeDecide.Group(Catalog, Set("command_pipeline.plain", "store.plain"))?.Id, Is.EqualTo("cqrs_system"));
        Assert.That(ComposeDecide.Group(Catalog, Set("command_pipeline.plain")), Is.Null);
        Assert.That(ComposeDecide.Group(Catalog, Set("observer.plain", "observer.plain", "observer.plain")), Is.Null, "cqrs_system takes two observers at most");
    }

    [Test]
    public async Task Hand_rule_001_with_a_script_gives_the_pipeline_and_the_store_in_a_system()
    {
        var judge = new ScriptedJudge(call => call.Agent switch
        {
            "decomposer" => TopologyTests.Needs(["Request"], ["Saved"], "save the changes that the user sends"),
            "composer-decider" when !call.Input.Contains("Remainder:", StringComparison.Ordinal) => TopologyTests.GradeTurn("query_handler.plain"),
            "composer-decider" when !call.Input.Contains("command_pipeline.plain, store.plain]", StringComparison.Ordinal) => TopologyTests.GradeTurn("command_pipeline.plain", "store.plain"),
            "composer-decider" => new { action = "commit", commit = new[] { new { spec = "request", set = new[] { "command_pipeline.plain", "store.plain" }, reason = "saves" } }, note = "commit" },
            "grader" => TopologyTests.Grade(call, call.Input.Contains("query_handler", StringComparison.Ordinal) ? ["job-1"] : []),
            "composer" => new { fragment = TopologyReference.Block(call.Input.Contains("Pattern: store", StringComparison.Ordinal) ? "store" : "command_pipeline", "plain")!.ToJsonString() },
            _ => null,
        });
        var c = new HandSourceCase("hand-rule-001");
        var (run, _) = await RunAsync(c.Request, judge, new NoUser());
        Assert.That(run.Trace.Tree!.ToString(), Does.Contain("cqrs_system").And.Contain("command_pipeline").And.Contain("store"));
        var first = ((ComposeTrace)run.Trace.Compose!.Loop!).Turns[0].Grades.Single();
        Assert.That(first.Remainder, Is.EqualTo(new[] { "io", "job-1" }), "code finds the missing Saved output; the grader finds the job");
        var grades = RunGrading.Grade(c.Case, Pack, run.Catalog, run.Trace, File.ReadAllText(run.OutputFile), GraderSet.Default.Plan(PackSettings.Section("grading"), Pack));
        Assert.That(grades.Verdicts.Where(v => !v.Value.Ok).Select(v => $"{v.Key}: {v.Value.Failures[0]}"), Is.Empty);
        Assert.That(judge.Calls.Any(cl => cl.Agent == "decider"), Is.False, "the status decider is not used");
    }

    [Test]
    public async Task A_missing_value_of_the_spec_goes_to_the_ask_step()
    {
        var asked = 0;
        var judge = new ScriptedJudge(call => call.Agent switch
        {
            "decomposer" when !call.Input.Contains("Answers of the user", StringComparison.Ordinal) => new { needs = Array.Empty<object>(), missing = new[] { "What does the design give at the end?" }, note = "unclear" },
            _ => null,
        });
        var (run, _) = await RunAsync("Design a part for orders.", judge, new CountingUser(() => asked++, null));
        Assert.That(asked, Is.EqualTo(1));
        Assert.That(run.Trace.Outcome, Is.EqualTo(Outcomes.Ask));
        Assert.That(run.Trace.Questions.Single().Text, Is.EqualTo("What does the design give at the end?"));
    }

    [Test]
    public async Task An_answer_of_the_user_goes_back_to_the_decomposer()
    {
        var judge = new ScriptedJudge(call => call.Agent switch
        {
            "decomposer" when !call.Input.Contains("Answers of the user", StringComparison.Ordinal) => new { needs = Array.Empty<object>(), missing = new[] { "What does the design give?" }, note = "unclear" },
            _ => null,
        });
        await RunAsync("Design a part for orders.", judge, new CountingUser(() => { }, "a saved order"));
        var second = judge.Calls.Where(c => c.Agent == "decomposer").ElementAt(1).Input;
        Assert.That(second, Does.Contain("Answers of the user: What does the design give? a saved order"));
    }

    private static IReadOnlyList<CatalogEntry> Set(params string[] ids)
    {
        var all = EngineComposeTask.Entries(Catalog).ToDictionary(e => e.Id);
        return ids.Select(i => all[i]).ToList();
    }

    private static async Task<(EngineRunResult, string)> RunAsync(string request, ScriptedJudge judge, IUserChannel user)
    {
        var pack = new PackContext(Pack, Dir, PackSettings, Catalog);
        var dir = Path.Combine(Path.GetTempPath(), "pe-tests", Guid.NewGuid().ToString("N")[..8]);
        EngineRun.Prepare(pack, dir);
        return (await EngineRun.RunAsync(pack, request, dir, EngineSettings.From(PackSettings), judge, user, CancellationToken.None), dir);
    }

    private sealed class CountingUser(Action count, string? answer) : IUserChannel
    {
        public Task<string?> AskAsync(Question question, CancellationToken ct)
        {
            count();
            return Task.FromResult(answer);
        }
    }

    /// <summary>One hand case of the topology pack, from cases/hand.yaml.</summary>
    private sealed class HandSourceCase(string id)
    {
        public Case Case { get; } = new Features.Cases.Domain.HandSource().Load(new Features.Cases.Domain.CaseContext(Dir, Catalog, null!)).Single(c => c.Id == id);

        public string Request => Case.Request;
    }
}
