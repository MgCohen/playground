using System.Text.Json;
using PatternEngine.Domains.Party;
using PatternEngine.Features.Compose.Domain;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Plugins.Agents;

namespace PatternEngine.Tests.Party;

/// <summary>Rung 6: the run sees prose only. The decomposer makes the spec; code checks it. No model.</summary>
public sealed class ProseTests
{
    private static readonly PartyPack Pack = PartyScript.Pack;

    private static readonly DecomposeRanges Ranges = DecomposeRanges.From(Pack.Settings.Section("party", "decomposer"));

    [Test]
    public void A_correct_decomposition_equals_the_hidden_rubrics()
    {
        var (anchor, errors) = Build("p1", new { tags = new[] { "fighter", "healer" }, stats = new[] { new { stat = "strength", min = 15 } }, budget = 12, note = "orcs" });
        Assert.That(errors, Is.Empty);
        Assert.That(anchor!.Errors, Is.Empty);
        Assert.That(PartyEval.Compare(Pack.Case("p1").Anchor, anchor), Is.EqualTo((0, 0, 0)));
        Assert.That(anchor.Rubrics.Select(r => r.Ask), Is.EqualTo(Pack.Case("p1").Anchor.Rubrics.Select(r => r.Ask)));
    }

    [Test]
    public void An_unknown_tag_is_refused()
    {
        Assert.That(Build("p1", new { tags = new[] { "fighter", "berserker" }, stats = Array.Empty<object>(), budget = 12, note = "" }).Errors, Is.EqualTo(new[] { "unknown tag \"berserker\"" }));
    }

    [Test]
    public void An_unknown_stat_is_refused()
    {
        Assert.That(Build("p1", new { tags = new[] { "fighter" }, stats = new[] { new { stat = "luck", min = 5 } }, budget = 12, note = "" }).Errors, Is.EqualTo(new[] { "unknown stat \"luck\"" }));
    }

    [TestCase(0)]
    [TestCase(61)]
    public void A_stat_amount_out_of_range_is_refused(int min)
    {
        Assert.That(Build("p1", new { tags = new[] { "fighter" }, stats = new[] { new { stat = "strength", min } }, budget = 12, note = "" }).Errors, Is.EqualTo(new[] { "stat \"strength\": the amount must be from 1 to 60" }));
    }

    [Test]
    public void A_budget_out_of_range_is_refused()
    {
        Assert.That(Build("p1", new { tags = new[] { "fighter" }, stats = Array.Empty<object>(), budget = 101, note = "" }).Errors, Is.EqualTo(new[] { "budget: the amount must be from 0 to 100" }));
    }

    [Test]
    public async Task A_run_decomposes_with_lore_then_composes_and_the_spec_matches()
    {
        var (result, judge) = await RunAsync("p4", "decompose", true,
            new { tags = new[] { "tank" }, stats = new[] { new { stat = "strength", min = 8 } }, budget = 5, note = "guard a gate" },
            Grade("p4", "guard", "squire"),
            Commit("p4", "guard", "squire"));
        var run = result.Runs.Single();
        Assert.That(judge.Calls.First().Agent, Is.EqualTo(Decomposer.Agent));
        Assert.That(judge.Calls.First().Input, Does.Contain("# Lore"));
        Assert.That(judge.Calls.First().Input, Does.Contain("Guard the gate of a small town for one night. 5 gold."));
        Assert.That(judge.Calls.First(c => c.Agent == ComposeLoop.DeciderAgent).Input, Does.Contain("- strength: Is the total strength of the party 8 or more?"));
        Assert.That(run.Pass, Is.True);
        Assert.That(run.Verdicts.Single(v => v.Rubric == "spec").Ok, Is.True);
        Assert.That(run.Verdicts.Single(v => v.Rubric == "best-cost").Reason, Is.EqualTo("cost 3, best 3"));
    }

    [Test]
    public async Task With_lore_off_the_decomposer_gets_no_lore_and_a_missed_stat_shows_in_the_spec()
    {
        var (result, judge) = await RunAsync("p1", "decompose", false,
            new { tags = new[] { "fighter", "healer" }, stats = Array.Empty<object>(), budget = 12, note = "no lore" },
            Commit("p1", "cleric", "squire"));
        Assert.That(judge.Calls.First().Input, Does.Not.Contain("# Lore"));
        var run = result.Runs.Single();
        Assert.That(run.Measures["spec"], Is.EqualTo("1 missed, 0 extra, 0 wrong amount"));
        Assert.That(run.Pass, Is.False);
        Assert.That(run.Verdicts.Single(v => v.Rubric == "valid").Reason, Is.EqualTo("p1: strength is false"));
    }

    [Test]
    public async Task A_refused_decomposition_is_retried_with_the_errors()
    {
        var (result, judge) = await RunAsync("p2", "decompose", true,
            new { tags = new[] { "thief", "mage", "lore" }, stats = Array.Empty<object>(), budget = 9, note = "x" },
            new { tags = new[] { "lockpick", "mage", "lore" }, stats = Array.Empty<object>(), budget = 9, note = "x" },
            Commit("p2", "thief", "apprentice", "sage"));
        var calls = judge.Calls.Where(c => c.Agent == Decomposer.Agent).ToList();
        Assert.That(calls, Has.Count.EqualTo(2));
        Assert.That(calls[1].Input, Does.Contain("- unknown tag \"thief\""));
        Assert.That(result.Runs.Single().Decomposition!.Attempts, Is.EqualTo(2));
        Assert.That(result.Runs.Single().Pass, Is.True);
    }

    [Test]
    public async Task Two_refused_decompositions_stop_the_run_with_no_commit()
    {
        var bad = new { tags = new[] { "thief" }, stats = Array.Empty<object>(), budget = 9, note = "x" };
        var (result, judge) = await RunAsync("p2", "decompose", true, bad, bad);
        var run = result.Runs.Single();
        Assert.That(run.Trace.Outcome, Is.EqualTo("stopped"));
        Assert.That(run.Trace.StopReason, Is.EqualTo("the decomposer gave no valid spec"));
        Assert.That(run.Pass, Is.False);
        Assert.That(judge.Calls.Any(c => c.Agent == ComposeLoop.DeciderAgent), Is.False);
    }

    [Test]
    public async Task The_given_mode_sends_the_hidden_rubrics_to_the_decider_with_no_decomposer()
    {
        var (result, judge) = await RunAsync("p3", "given", true, Commit("p3", "ranger", "cleric"));
        Assert.That(judge.Calls.Any(c => c.Agent == Decomposer.Agent), Is.False);
        var decider = judge.Calls.First(c => c.Agent == ComposeLoop.DeciderAgent).Input;
        Assert.That(decider, Does.Contain("Follow a lost caravan into the forest and help the wounded. 9 gold."));
        Assert.That(decider, Does.Contain("- tracker: Does the party have a tracker?"));
        Assert.That(decider, Does.Not.Contain("The run sees only the text"));
        Assert.That(result.Runs.Single().Pass, Is.True);
        Assert.That(result.Runs.Single().Verdicts.Any(v => v.Rubric == "spec"), Is.False);
        Assert.That(result.Runs.Single().Decomposition!.Mode, Is.EqualTo("given"));
        Assert.That(Pack.Case("p3").Anchor.Rubrics, Has.All.Property("Hidden").True, "the case file keeps its eval rubrics");
    }

    [Test]
    public void The_decomposer_agent_file_loads()
    {
        Assert.That(new AgentCatalog([Support.Repo.Path("agents")]).Get(Decomposer.Agent).Name, Is.EqualTo("decomposer"));
    }

    private static (Features.Compose.Domain.Shared.Anchor? Anchor, IReadOnlyList<string> Errors) Build(string id, object answer) =>
        Decomposer.Build(Pack, Ranges, id, Pack.Case(id).Text!, new AgentAnswer(Needs(answer), 0, 0, null));

    /// <summary>Writes a short test answer (tags, stats, budget) in the generic decomposer schema: needs, missing, note.</summary>
    private static JsonElement Needs(object answer)
    {
        var a = JsonSerializer.SerializeToElement(answer);
        var needs = a.GetProperty("tags").EnumerateArray().Select(t => (object)new { kind = "tag", name = t.GetString(), amount = (int?)null })
            .Concat(a.GetProperty("stats").EnumerateArray().Select(s => (object)new { kind = "stat", name = s.GetProperty("stat").GetString(), amount = (int?)s.GetProperty("min").GetInt32() }))
            .Concat(a.GetProperty("budget").ValueKind == JsonValueKind.Number ? [new { kind = "budget", name = "budget", amount = (int?)a.GetProperty("budget").GetInt32() }] : [])
            .ToArray();
        return JsonSerializer.SerializeToElement(new { needs, missing = Array.Empty<string>(), note = a.GetProperty("note").GetString() });
    }

    private static async Task<(PartyRunResult, ScriptedJudge)> RunAsync(string id, string spec, bool lore, params object[] answers)
    {
        var decomposer = new Queue<object>(answers.Where(a => a.GetType().GetProperty("tags") is not null));
        var decider = new Queue<object>(answers.Where(a => a.GetType().GetProperty("tags") is null));
        var judge = new ScriptedJudge(call => call.Agent switch
        {
            ComposeLoop.GraderAgent => PartyScript.OracleGrader(call),
            Decomposer.Agent => decomposer.Count > 0 ? Needs(decomposer.Dequeue()) : null,
            _ => decider.Count > 0 ? decider.Dequeue() : null,
        });
        var runner = new PartyRunner(Pack, () => judge, new RunLimits(80, 1.0, 60), Path.Combine(Path.GetTempPath(), "pe-tests"));
        var result = await runner.RunAsync(new PartyRunOptions("dev", "haiku", 1, [id], "compare", "test", spec, lore), _ => { }, CancellationToken.None);
        return (result, judge);
    }

    private static object Grade(string spec, params string[] set) => new { action = "grade", grade = new[] { new { spec, set } }, note = "test" };

    private static object Commit(string spec, params string[] set) => new { action = "commit", commit = new[] { new { spec, set, reason = "test" } }, note = "test" };
}
