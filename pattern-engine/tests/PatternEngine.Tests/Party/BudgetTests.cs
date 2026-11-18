using PatternEngine.Domains.Party;
using PatternEngine.Features.Compose.Domain;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Plugins.Agents;

namespace PatternEngine.Tests.Party;

/// <summary>Rung 2: budget. The loop with scripted deciders for the greedy and compare strategies. No model.</summary>
public sealed class BudgetTests
{
    private static readonly PartyPack Pack = PartyScript.Pack;

    private static readonly PartyEval Eval = new(Pack);

    [Test]
    public async Task Compare_grades_the_candidates_in_one_turn_and_commits_the_cheapest()
    {
        var (trace, judge) = await RunAsync("b1", "compare",
            new
            {
                action = "grade",
                grade = new[] { new[] { "paladin" }, new[] { "cleric", "knight" }, new[] { "cleric", "guard", "squire" } }.Select(set => new { spec = "b1", set }).ToArray(),
                note = "three candidates",
            },
            Commit("b1", "cleric", "guard", "squire"));
        Assert.That(trace.Turns[0].Grades, Has.Count.EqualTo(3));
        Assert.That(trace.Turns[0].Grades.All(g => g.Remainder.Count == 0), Is.True);
        Assert.That(judge.Calls.Count(c => c.Agent == ComposeLoop.GraderAgent), Is.EqualTo(3));
        var result = Eval.Grade(Pack.Case("b1"), [trace.CommitOf("b1")]);
        Assert.That(result.Pass, Is.True);
        Assert.That(result.Of("best-cost")!.Ok, Is.True);
    }

    [Test]
    public async Task Greedy_pays_9_for_b1_and_fails_best_cost()
    {
        var (trace, _) = await RunAsync("b1", "greedy", Grade("b1", "paladin"), Commit("b1", "paladin"));
        var result = Eval.Grade(Pack.Case("b1"), [trace.CommitOf("b1")]);
        Assert.That(result.Pass, Is.True);
        Assert.That(result.Of("best-cost")!.Reason, Is.EqualTo("cost 9, best 6"));
    }

    [Test]
    public async Task The_grader_finds_a_party_over_the_budget_and_code_does_not_refuse_it()
    {
        var (trace, _) = await RunAsync("b2", "greedy", Grade("b2", "champion"), Commit("b2", "champion"));
        Assert.That(trace.Turns[0].Refused, Is.Empty);
        Assert.That(trace.Turns[0].Grades[0].Remainder, Is.EqualTo(new[] { "budget" }));
        Assert.That(Eval.Grade(Pack.Case("b2"), [trace.CommitOf("b2")]).Of("valid")!.Reason, Is.EqualTo("b2: budget is false"));
    }

    [Test]
    public async Task None_is_correct_for_b4()
    {
        var (trace, _) = await RunAsync("b4", "compare", Grade("b4", "cleric", "guard", "thief", "apprentice"), Commit("b4", null));
        Assert.That(trace.Turns[0].Grades[0].Remainder, Is.EqualTo(new[] { "budget" }));
        var result = Eval.Grade(Pack.Case("b4"), [trace.CommitOf("b4")]);
        Assert.That(result.Pass, Is.True);
        Assert.That(result.Of("best-cost")!.Reason, Is.EqualTo("none is correct"));
    }

    [Test]
    public void The_budget_rung_shows_the_cost_and_the_compare_strategy()
    {
        var task = new PartyTask(Pack, Pack.Case("b3"), PartyTask.FieldsOf(Pack, "budget"), "compare");
        Assert.That(task.Catalog, Does.Contain("- sage: tags lore; cost 2"));
        Assert.That(task.Catalog, Does.Not.Contain("strength"));
        Assert.That(task.Strategy, Does.StartWith("Compare."));
    }

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

    private static object Commit(string spec, params string[]? set) => new { action = "commit", commit = new[] { new { spec, set, reason = "test" } }, note = "test" };
}
