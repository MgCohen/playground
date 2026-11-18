using PatternEngine.Domains.Party;
using PatternEngine.Features.Compose.Domain;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Plugins.Agents;

namespace PatternEngine.Tests.Party;

/// <summary>Rung 5: stat minimums. A party covers the tags but must also reach each stat total. No model.</summary>
public sealed class StatsTests
{
    private static readonly PartyPack Pack = PartyScript.Pack;

    private static readonly PartyEval Eval = new(Pack);

    [Test]
    public async Task A_stat_below_the_minimum_stays_in_the_remainder_until_a_hero_closes_it()
    {
        var (trace, _) = await RunAsync("s1", Grade("s1", "barbarian", "squire"), Grade("s1", "guard", "barbarian", "squire"), Commit("s1", "guard", "barbarian", "squire"));
        Assert.That(trace.Turns[0].Grades[0].Remainder, Is.EqualTo(new[] { "strength" }));
        Assert.That(trace.Turns[1].Grades[0].Remainder, Is.Empty);
        var result = Eval.Grade(Pack.Case("s1"), [trace.CommitOf("s1")]);
        Assert.That(result.Pass, Is.True);
        Assert.That(result.Of("best-cost")!.Ok, Is.True);
    }

    [Test]
    public async Task S3_reaches_knowledge_10_exactly_with_the_best_party()
    {
        var (trace, _) = await RunAsync("s3", Grade("s3", "cleric", "guard", "hunter", "squire"), Commit("s3", "cleric", "guard", "hunter", "squire"));
        Assert.That(trace.Turns[0].Grades[0].Remainder, Is.Empty);
        Assert.That(Eval.Grade(Pack.Case("s3"), [trace.CommitOf("s3")]).Of("best-cost")!.Reason, Is.EqualTo("cost 8, best 8"));
    }

    [Test]
    public async Task Another_best_party_of_s1_also_gets_best_cost()
    {
        var other = Eval.Oracle.Best(Pack.Case("s1").Quests[0]).Cheapest.First(p => !p.SequenceEqual(["barbarian", "guard", "squire"]));
        var (trace, _) = await RunAsync("s1", Commit("s1", [.. other]));
        Assert.That(Eval.Grade(Pack.Case("s1"), [trace.CommitOf("s1")]).Of("best-cost")!.Ok, Is.True);
    }

    [Test]
    public void The_stats_rung_shows_the_stats_to_the_agents()
    {
        var task = new PartyTask(Pack, Pack.Case("s2"), PartyTask.FieldsOf(Pack, "stats"), "compare");
        Assert.That(task.Catalog, Does.Contain("- sage: tags lore; cost 2; strength 1, intelligence 6, agility 1, knowledge 9"));
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

    private static object Commit(string spec, params string[] set) => new { action = "commit", commit = new[] { new { spec, set, reason = "test" } }, note = "test" };
}
