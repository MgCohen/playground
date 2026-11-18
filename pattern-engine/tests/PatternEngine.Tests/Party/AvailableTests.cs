using PatternEngine.Domains.Party;
using PatternEngine.Features.Compose.Domain;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Plugins.Agents;

namespace PatternEngine.Tests.Party;

/// <summary>Rung 3: heroes that are not free. Code does not refuse them: the available rubric is the work of the agents.</summary>
public sealed class AvailableTests
{
    private static readonly PartyPack Pack = PartyScript.Pack;

    private static readonly PartyEval Eval = new(Pack);

    [Test]
    public async Task The_best_party_of_b1_is_not_free_in_a1_so_the_best_changes()
    {
        var (trace, _) = await RunAsync("a1", Grade("a1", "cleric", "guard", "squire"), Grade("a1", "paladin"), Commit("a1", "paladin"));
        Assert.That(trace.Turns[0].Refused, Is.Empty);
        Assert.That(trace.Turns[0].Grades[0].Remainder, Is.EqualTo(new[] { "available" }));
        var result = Eval.Grade(Pack.Case("a1"), [trace.CommitOf("a1")]);
        Assert.That(result.Pass, Is.True);
        Assert.That(result.Of("best-cost")!.Reason, Is.EqualTo("cost 9, best 9"));
    }

    [Test]
    public async Task A_commit_with_a_hero_that_is_not_free_fails_valid()
    {
        var (trace, _) = await RunAsync("a2", Commit("a2", "apprentice", "sage"));
        Assert.That(trace.Outcome, Is.EqualTo("committed"));
        Assert.That(Eval.Grade(Pack.Case("a2"), [trace.CommitOf("a2")]).Of("valid")!.Reason, Is.EqualTo("a2: available is false"));
    }

    [Test]
    public async Task None_is_correct_for_a3_with_the_only_lockpick_not_free()
    {
        var (trace, _) = await RunAsync("a3", Commit("a3", null));
        Assert.That(Eval.Grade(Pack.Case("a3"), [trace.CommitOf("a3")]).Pass, Is.True);
    }

    [Test]
    public void The_decider_sees_the_heroes_that_are_not_free()
    {
        var quest = Pack.Case("a1").Quests[0];
        Assert.That(quest.Context, Does.Contain("Not free: cleric."));
        Assert.That(quest.Visible.Single(r => r.Id == "available").Ask, Does.Contain("(cleric)"));
    }

    private static async Task<(ComposeTrace, ScriptedJudge)> RunAsync(string id, params object[] turns)
    {
        var queue = new Queue<object>(turns);
        var judge = new ScriptedJudge(call => call.Agent == ComposeLoop.GraderAgent ? PartyScript.OracleGrader(call) : queue.Count > 0 ? queue.Dequeue() : null);
        var c = Pack.Case(id);
        var settings = ComposeSettings.From(Pack.Settings) with { Strategy = "compare" };
        var trace = await new ComposeLoop(new PartyTask(Pack, c, PartyTask.FieldsOf(Pack, c.Rung), "compare"), settings, new AgentGateway(judge, Path.GetTempPath(), new RunLimits(80, 1.0, 60))).RunAsync(CancellationToken.None);
        return (trace, judge);
    }

    private static object Grade(string spec, params string[] set) => new { action = "grade", grade = new[] { new { spec, set } }, note = "test" };

    private static object Commit(string spec, params string[]? set) => new { action = "commit", commit = new[] { new { spec, set, reason = "test" } }, note = "test" };
}
