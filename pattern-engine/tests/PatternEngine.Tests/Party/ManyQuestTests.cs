using PatternEngine.Domains.Party;
using PatternEngine.Features.Compose.Domain;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Plugins.Agents;

namespace PatternEngine.Tests.Party;

/// <summary>Rung 4: many quests share one pool. An assignment grade checks the pool rubric. No model.</summary>
public sealed class ManyQuestTests
{
    private static readonly PartyPack Pack = PartyScript.Pack;

    private static readonly PartyEval Eval = new(Pack);

    [Test]
    public async Task Pool_grades_the_parties_and_the_assignment_in_one_turn()
    {
        var (trace, judge) = await RunAsync("m1", "pool",
            new
            {
                action = "grade",
                grade = new[] { new { spec = "m1-q1", set = new[] { "druid" } }, new { spec = "m1-q2", set = new[] { "cleric", "guard" } } },
                assignment = new[] { new { spec = "m1-q1", set = new[] { "druid" } }, new { spec = "m1-q2", set = new[] { "cleric", "guard" } } },
                note = "scarce cleric to q2",
            },
            Commit(("m1-q1", ["druid"]), ("m1-q2", ["cleric", "guard"])));
        Assert.That(trace.Turns[0].Grades, Has.Count.EqualTo(3));
        var assignment = trace.Turns[0].Grades.Single(g => g.Parts is not null);
        Assert.That(assignment.Spec, Is.EqualTo("m1"));
        Assert.That(assignment.Remainder, Is.Empty);
        Assert.That(judge.Calls.Single(c => c.Input.Contains("# Sets", StringComparison.Ordinal)).Input, Does.Contain("## m1-q2\n\n- cleric: tags healer; cost 3\n- guard: tags tank; cost 2"));
        var result = Eval.Grade(Pack.Case("m1"), Commits(trace, "m1"));
        Assert.That(result.Pass, Is.True);
        Assert.That(result.Of("best-total")!.Reason, Is.EqualTo("cost 12, best 12"));
    }

    [Test]
    public async Task In_order_takes_the_cleric_for_q1_and_fails_q2()
    {
        var (trace, _) = await RunAsync("m1", "in-order", Grade("m1-q1", "cleric", "sage"), Grade("m1-q2", "paladin"), Commit(("m1-q1", ["cleric", "sage"]), ("m1-q2", null)));
        Assert.That(trace.Turns[1].Grades[0].Remainder, Is.EqualTo(new[] { "budget" }));
        var result = Eval.Grade(Pack.Case("m1"), Commits(trace, "m1"));
        Assert.That(result.Pass, Is.False);
        Assert.That(result.Of("valid")!.Reason, Is.EqualTo("m1-q2: none, but a valid party exists"));
    }

    [Test]
    public async Task A_hero_in_two_parties_is_in_the_remainder_of_the_assignment_and_code_does_not_refuse_it()
    {
        var both = new[] { new { spec = "m3-q1", set = new[] { "cleric" } }, new { spec = "m3-q2", set = new[] { "cleric" } } };
        var (trace, _) = await RunAsync("m3", "pool", new { action = "grade", assignment = both, note = "one cleric" }, Commit(("m3-q1", ["cleric"]), ("m3-q2", ["cleric"])));
        Assert.That(trace.Turns[0].Grades.Single().Remainder, Is.EqualTo(new[] { "pool" }));
        Assert.That(trace.Outcome, Is.EqualTo("committed"));
        Assert.That(Eval.Grade(Pack.Case("m3"), Commits(trace, "m3")).Pass, Is.False);
    }

    [Test]
    public async Task None_for_each_quest_is_correct_for_m3()
    {
        var (trace, _) = await RunAsync("m3", "pool", Commit(("m3-q1", null), ("m3-q2", null)));
        Assert.That(Eval.Grade(Pack.Case("m3"), Commits(trace, "m3")).Pass, Is.True);
    }

    [Test]
    public async Task An_assignment_must_give_a_set_or_null_for_each_spec()
    {
        var (trace, _) = await RunAsync("m2", "pool", new { action = "grade", assignment = new[] { new { spec = "m2-q1", set = new[] { "ranger" } } }, note = "part" }, Commit(("m2-q1", ["ranger"]), ("m2-q2", ["thief"]), ("m2-q3", ["bard"])));
        Assert.That(trace.Turns[0].Refused.Single(), Is.EqualTo("assignment: give one set (or null) for each spec; missing m2-q2, m2-q3"));
        Assert.That(trace.Grades, Is.Zero);
    }

    [Test]
    public async Task An_assignment_is_refused_for_a_task_with_one_spec()
    {
        var (trace, _) = await RunAsync("c1", "greedy", new { action = "grade", assignment = new[] { new { spec = "c1", set = new[] { "paladin" } } }, note = "x" }, Commit(("c1", ["paladin"])));
        Assert.That(trace.Turns[0].Refused.Single(), Is.EqualTo("assignment: this task has no rubrics over all sets"));
    }

    [Test]
    public async Task The_same_assignment_twice_uses_the_cache()
    {
        var parts = new[] { new { spec = "m2-q1", set = new[] { "ranger" } }, new { spec = "m2-q2", set = new[] { "thief" } }, new { spec = "m2-q3", set = new[] { "bard" } } };
        var (trace, judge) = await RunAsync("m2", "pool", new { action = "grade", assignment = parts, note = "a" }, new { action = "grade", assignment = parts, note = "b" }, Commit(("m2-q1", ["ranger"]), ("m2-q2", ["thief"]), ("m2-q3", ["bard"])));
        Assert.That(judge.Calls.Count(c => c.Agent == ComposeLoop.GraderAgent), Is.EqualTo(1));
        Assert.That(trace.CacheHits, Is.EqualTo(1));
        Assert.That(Eval.Grade(Pack.Case("m2"), Commits(trace, "m2")).Of("best-total")!.Ok, Is.True);
    }

    [Test]
    public void The_decider_sees_the_rubrics_over_all_sets()
    {
        var task = new PartyTask(Pack, Pack.Case("m1"), PartyTask.FieldsOf(Pack, "many"), "pool");
        Assert.That(task.Whole!.Visible.Single().Id, Is.EqualTo("pool"));
        Assert.That(task.Specs.Select(s => s.Id), Is.EqualTo(new[] { "m1-q1", "m1-q2" }));
    }

    private static IReadOnlyList<IReadOnlyList<string>?> Commits(ComposeTrace trace, string id) =>
        Pack.Case(id).Quests.Select(q => trace.CommitOf(q.Id)).ToList();

    private static async Task<(ComposeTrace, ScriptedJudge)> RunAsync(string id, string strategy, params object[] turns)
    {
        var queue = new Queue<object>(turns);
        var judge = new ScriptedJudge(call => call.Agent == ComposeLoop.GraderAgent ? PartyScript.OracleGrader(call) : queue.Count > 0 ? queue.Dequeue() : null);
        var c = Pack.Case(id);
        var settings = ComposeSettings.From(Pack.Settings) with { Strategy = strategy };
        var trace = await new ComposeLoop(new PartyTask(Pack, c, PartyTask.FieldsOf(Pack, c.Rung), strategy), settings, new AgentGateway(judge, Path.GetTempPath(), new RunLimits(80, 1.0, 60))).RunAsync(CancellationToken.None);
        return (trace, judge);
    }

    private static object Grade(string spec, params string[] set) => new { action = "grade", grade = new[] { new { spec, set } }, note = "test" };

    private static object Commit(params (string Spec, string[]? Set)[] commits) =>
        new { action = "commit", commit = commits.Select(c => new { spec = c.Spec, set = c.Set, reason = "test" }).ToArray(), note = "test" };
}
