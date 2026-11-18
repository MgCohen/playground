using PatternEngine.Domains.Party;
using PatternEngine.Features.Compose.Domain;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Plugins.Agents;
using PatternEngine.Tests.Support;

namespace PatternEngine.Tests.Party;

/// <summary>The compose loop with a scripted decider and a grader that answers with the oracle truth. No model.</summary>
public sealed class ComposeLoopTests
{
    private static readonly PartyPack Pack = PartyScript.Pack;

    private static readonly ComposeSettings Settings = ComposeSettings.From(Pack.Settings);

    [Test]
    public async Task Greedy_turns_grade_and_commit_the_best_party_of_b1()
    {
        var (trace, judge) = await RunAsync("b1",
            Grade("b1", "cleric"),
            Grade("b1", "cleric", "guard"),
            Grade("b1", "cleric", "guard", "squire"),
            Commit(("b1", ["cleric", "guard", "squire"])));
        Assert.That(trace.Outcome, Is.EqualTo("committed"));
        Assert.That(trace.CommitOf("b1"), Is.EqualTo(new[] { "cleric", "guard", "squire" }));
        Assert.That(trace.Grades, Is.EqualTo(3));
        Assert.That(trace.Turns[0].Grades[0].Remainder, Is.EqualTo(new[] { "tank", "fighter" }));
        Assert.That(trace.Turns[2].Grades[0].Remainder, Is.Empty);
        Assert.That(judge.Calls.Count(c => c.Agent == "grader"), Is.EqualTo(3));
        Assert.That(new PartyEval(Pack).Grade(Pack.Case("b1"), [trace.CommitOf("b1")]).Pass, Is.True);
    }

    [Test]
    public async Task The_decider_input_shows_the_remainder_of_each_graded_party()
    {
        var (_, judge) = await RunAsync("b1", Grade("b1", "cleric"), Commit(("b1", null)));
        var second = judge.Calls.Where(c => c.Agent == ComposeLoop.DeciderAgent).ElementAt(1).Input;
        Assert.That(second, Does.Contain("- spec b1, set [cleric]: healer yes; tank no; fighter no; budget yes. Remainder: tank, fighter"));
        Assert.That(second, Does.Contain("Turn 2 of 8"));
        Assert.That(second, Does.Contain("Grades left in the run: 39"));
    }

    [Test]
    public async Task The_same_party_twice_uses_the_cache()
    {
        var (trace, judge) = await RunAsync("c1", Grade("c1", "paladin"), Grade("c1", "paladin"), Commit(("c1", ["paladin"])));
        Assert.That(judge.Calls.Count(c => c.Agent == "grader"), Is.EqualTo(1));
        Assert.That(trace.CacheHits, Is.EqualTo(1));
        Assert.That(trace.Grades, Is.EqualTo(1));
        Assert.That(trace.Turns[1].Grades[0].Cached, Is.True);
    }

    [Test]
    public async Task Grades_over_the_limit_of_one_turn_are_refused()
    {
        var parties = Pack.Heroes.Heroes.Take(10).Select(h => new[] { h.Id }).ToArray();
        var (trace, judge) = await RunAsync("c1", new { action = "grade", grade = parties.Select(p => new { spec = "c1", set = p }).ToArray(), note = "all" }, Commit(("c1", ["paladin"])));
        Assert.That(trace.Turns[0].Grades, Has.Count.EqualTo(Settings.Limits.GradesPerTurn));
        Assert.That(trace.Turns[0].Refused, Has.Count.EqualTo(10 - Settings.Limits.GradesPerTurn));
        Assert.That(trace.Turns[0].Refused[0], Does.Contain("grades in one turn"));
        Assert.That(judge.Calls.Count(c => c.Agent == "grader"), Is.EqualTo(Settings.Limits.GradesPerTurn));
    }

    [Test]
    public async Task Grades_over_the_limit_of_the_run_are_refused()
    {
        var settings = Settings with { Limits = Settings.Limits with { Grades = 2 } };
        var (trace, _) = await RunAsync("c1", settings, Grade("c1", "cleric"), Grade("c1", "guard"), Grade("c1", "knight"), Commit(("c1", ["paladin"])));
        Assert.That(trace.Grades, Is.EqualTo(2));
        Assert.That(trace.Turns[2].Refused.Single(), Does.Contain("limit of 2 grades in the run"));
    }

    [Test]
    public async Task No_commit_in_the_turn_limit_commits_none()
    {
        var settings = Settings with { Limits = Settings.Limits with { Turns = 2 } };
        var (trace, _) = await RunAsync("c1", settings, Grade("c1", "cleric"), Grade("c1", "guard"));
        Assert.That(trace.Outcome, Is.EqualTo("no-commit"));
        Assert.That(trace.StopReason, Does.Contain("turn limit"));
        Assert.That(trace.CommitOf("c1"), Is.Null);
    }

    [Test]
    public async Task An_unknown_hero_is_refused_and_named_in_the_next_turn()
    {
        var (trace, judge) = await RunAsync("c1", Grade("c1", "paladin", "dragon"), Commit(("c1", ["dragon"])), Commit(("c1", ["paladin"])));
        Assert.That(trace.Turns[0].Refused.Single(), Does.Contain("unknown entry \"dragon\""));
        Assert.That(trace.Turns[1].Refused.Single(), Does.Contain("unknown entry \"dragon\""));
        Assert.That(trace.CommitOf("c1"), Is.EqualTo(new[] { "paladin" }));
        Assert.That(judge.Calls.Count(c => c.Agent == "grader"), Is.Zero);
        Assert.That(judge.Calls.Last(c => c.Agent == ComposeLoop.DeciderAgent).Input, Does.Contain("Turn 1: grade refused: unknown entry \"dragon\""));
    }

    [Test]
    public async Task A_party_over_the_size_limit_is_refused()
    {
        var settings = Settings with { Limits = Settings.Limits with { SetSize = 2 } };
        var (trace, _) = await RunAsync("c1", settings, Grade("c1", "cleric", "guard", "squire"), Commit(("c1", ["paladin"])));
        Assert.That(trace.Turns[0].Refused.Single(), Does.Contain("more than 2 entries"));
    }

    [Test]
    public async Task Code_does_not_refuse_a_party_over_the_budget()
    {
        var (trace, _) = await RunAsync("b1", Commit(("b1", ["champion"])));
        Assert.That(trace.Outcome, Is.EqualTo("committed"));
        Assert.That(new PartyEval(Pack).Grade(Pack.Case("b1"), [trace.CommitOf("b1")]).Pass, Is.False);
    }

    [Test]
    public async Task A_commit_of_none_passes_c5()
    {
        var (trace, _) = await RunAsync("c5", Grade("c5", "cleric"), Commit(("c5", null)));
        Assert.That(trace.CommitOf("c5"), Is.Null);
        Assert.That(trace.Commits.Single().Reason, Is.EqualTo("test"));
        Assert.That(new PartyEval(Pack).Grade(Pack.Case("c5"), [trace.CommitOf("c5")]).Pass, Is.True);
    }

    [Test]
    public async Task A_commit_must_cover_each_spec()
    {
        var (trace, _) = await RunAsync("m1", Commit(("m1-q1", ["druid"])), Commit(("m1-q1", ["druid"]), ("m1-q2", ["cleric", "guard"])));
        Assert.That(trace.Turns[0].Refused.Single(), Does.Contain("no commit for spec \"m1-q2\""));
        Assert.That(trace.CommitOf("m1-q2"), Is.EqualTo(new[] { "cleric", "guard" }));
    }

    [Test]
    public async Task A_missing_verdict_goes_to_the_remainder()
    {
        var judge = new ScriptedJudge(call => call.Agent == "grader"
            ? new { verdicts = new[] { new { rubric = "healer", verdict = true, reason = "paladin" } } }
            : Next(call));
        decider = new Queue<object>([Grade("c1", "paladin"), Commit(("c1", ["paladin"]))]);
        var trace = await new ComposeLoop(Task("c1"), Settings, Gateway(judge)).RunAsync(CancellationToken.None);
        Assert.That(trace.Turns[0].Grades[0].Remainder, Is.EqualTo(new[] { "tank" }));
        Assert.That(trace.Turns[0].Grades[0].Verdicts[1].Reason, Is.EqualTo("no verdict"));
    }

    [Test]
    public async Task A_bad_decider_answer_uses_one_turn()
    {
        var (trace, _) = await RunAsync("c1", new { action = "wait" }, Commit(("c1", ["paladin"])));
        Assert.That(trace.Turns[0].Refused.Single(), Does.Contain("unknown action"));
        Assert.That(trace.Outcome, Is.EqualTo("committed"));
    }

    [Test]
    public async Task The_run_limit_of_agent_calls_stops_the_loop()
    {
        decider = new Queue<object>([Grade("c1", "cleric"), Grade("c1", "guard"), Grade("c1", "knight")]);
        var judge = new ScriptedJudge(call => call.Agent == "grader" ? PartyScript.OracleGrader(call) : Next(call));
        var gateway = new AgentGateway(judge, Path.GetTempPath(), new RunLimits(3, 1.0, 60));
        var trace = await new ComposeLoop(Task("c1"), Settings, gateway).RunAsync(CancellationToken.None);
        Assert.That(trace.Outcome, Is.EqualTo("stopped"));
        Assert.That(trace.StopReason, Does.Contain("3 agent calls"));
        Assert.That(trace.CommitOf("c1"), Is.Null);
    }

    [Test]
    public void The_grader_sees_only_the_fields_of_the_rung()
    {
        var cover = Task("c1");
        Assert.That(cover.SetText(["paladin"]), Is.EqualTo("- paladin: tags healer, tank, fighter"));
        var stats = Task("s1");
        Assert.That(stats.SetText(["paladin"]), Is.EqualTo("- paladin: tags healer, tank, fighter; cost 9; strength 8, intelligence 4, agility 3, knowledge 5"));
    }

    private Queue<object> decider = new();

    private static PartyTask Task(string id) => new(Pack, Pack.Case(id), PartyTask.FieldsOf(Pack, Pack.Case(id).Rung), "greedy");

    private static AgentGateway Gateway(IJudge judge) => new(judge, Path.GetTempPath(), new RunLimits(80, 1.0, 60));

    private Task<(ComposeTrace, ScriptedJudge)> RunAsync(string id, params object[] turns) => RunAsync(id, Settings, turns);

    private async Task<(ComposeTrace, ScriptedJudge)> RunAsync(string id, ComposeSettings settings, params object[] turns)
    {
        decider = new Queue<object>(turns);
        var judge = new ScriptedJudge(call => call.Agent == "grader" ? PartyScript.OracleGrader(call) : Next(call));
        var trace = await new ComposeLoop(Task(id), settings, Gateway(judge)).RunAsync(CancellationToken.None);
        return (trace, judge);
    }

    private object? Next(AgentCall call) => decider.Count > 0 ? decider.Dequeue() : null;

    private static object Grade(string spec, params string[] set) => new { action = "grade", grade = new[] { new { spec, set } }, note = "test" };

    private static object Commit(params (string Spec, string[]? Set)[] commits) =>
        new { action = "commit", commit = commits.Select(c => new { spec = c.Spec, set = c.Set, reason = "test" }).ToArray(), note = "test" };
}
