using PatternEngine.Domains.Party;
using PatternEngine.Features.Compose.Domain.Shared;
using PatternEngine.Tests.Support;

namespace PatternEngine.Tests.Party;

/// <summary>The exact answers of the 22 cases of the composition plan, and the eval against mutations of a best party.</summary>
public sealed class OracleTests
{
    private static readonly PartyPack Pack = PartyPack.Load(Repo.Path("src", "PatternEngine", "Domains", "Party"), Repo.Path("pe.settings.yaml"));

    private static readonly PartyEval Eval = new(Pack);

    private static Oracle Oracle => Eval.Oracle;

    [Test]
    public void The_pack_loads_with_no_errors()
    {
        Assert.That(Pack.Errors, Is.Empty);
        Assert.That(Pack.Heroes.Heroes, Has.Count.EqualTo(16));
        Assert.That(Pack.Cases.Select(c => c.Id), Is.EquivalentTo(new[]
        {
            "c1", "c2", "c3", "c4", "c5", "b1", "b2", "b3", "b4", "a1", "a2", "a3", "m1", "m2", "m3", "s1", "s2", "s3", "p1", "p2", "p3", "p4",
        }));
    }

    [TestCase("c1", 1, "paladin")]
    [TestCase("c2", 1, "thief")]
    [TestCase("c3", 2, "paladin,ranger")]
    [TestCase("c4", 3, "archmage,hunter,thief")]
    public void Cover_cases_have_the_smallest_size(string id, int size, string example)
    {
        var best = Oracle.Best(Pack.Case(id).Quests[0]);
        Assert.That(best.SmallestSize, Is.EqualTo(size));
        Assert.That(best.Smallest.Select(Join), Does.Contain(example));
    }

    [TestCase("c5")]
    [TestCase("b4")]
    [TestCase("a3")]
    public void Cases_with_no_valid_party_answer_none(string id)
    {
        Assert.That(Oracle.Best(Pack.Case(id).Quests[0]).None, Is.True);
    }

    [TestCase("b1", 6, "cleric,guard,squire")]
    [TestCase("b2", 8, "cleric,guard,thief")]
    [TestCase("b3", 4, "apprentice,sage")]
    [TestCase("a1", 9, "paladin")]
    [TestCase("a2", 8, "sage,spellblade")]
    [TestCase("s1", 6, "barbarian,guard,squire")]
    [TestCase("s2", 6, "apprentice,hunter,sage")]
    [TestCase("s3", 8, "cleric,guard,hunter,squire")]
    public void One_quest_cases_have_the_best_cost(string id, int cost, string example)
    {
        var best = Oracle.Best(Pack.Case(id).Quests[0]);
        Assert.That(best.BestCost, Is.EqualTo(cost));
        Assert.That(best.Cheapest.Select(Join), Does.Contain(example));
    }

    [Test]
    public void B4_has_a_cheapest_cover_of_10_over_its_budget()
    {
        var quest = Pack.Case("b4").Quests[0];
        var noBudget = quest with { Rubrics = quest.Rubrics.Where(r => r.Id != "budget").ToList() };
        Assert.That(Oracle.Best(noBudget).BestCost, Is.EqualTo(10));
    }

    [Test]
    public void S2_has_one_party_only()
    {
        Assert.That(Oracle.Best(Pack.Case("s2").Quests[0]).ValidCount, Is.EqualTo(1));
    }

    [TestCase("s1")]
    [TestCase("s3")]
    public void S1_and_s3_have_more_than_one_best_party(string id)
    {
        Assert.That(Oracle.Best(Pack.Case(id).Quests[0]).Cheapest, Has.Count.GreaterThan(1));
    }

    [TestCase("p1", 7)]
    [TestCase("p2", 7)]
    [TestCase("p3", 7)]
    [TestCase("p4", 3)]
    public void Prose_cases_have_the_best_cost_of_the_hidden_rubrics(string id, int cost)
    {
        Assert.That(Oracle.Best(Pack.Case(id).Quests[0]).BestCost, Is.EqualTo(cost));
    }

    [TestCase("m1", 12, "druid|cleric,guard")]
    [TestCase("m2", 11, "ranger|thief|bard")]
    public void Many_quest_cases_have_the_best_total(string id, int total, string parties)
    {
        var best = Oracle.BestAssignment(Pack.Case(id));
        Assert.That(best.Total, Is.EqualTo(total));
        Assert.That(string.Join("|", best.Parties.Select(p => Join(p!))), Is.EqualTo(parties));
    }

    [Test]
    public void M3_has_no_assignment_with_one_cleric()
    {
        Assert.That(Oracle.BestAssignment(Pack.Case("m3")).Total, Is.Null);
    }

    [TestCase("m1", false)]
    [TestCase("m2", true)]
    [TestCase("m3", false)]
    public void Quests_in_order_fail_where_the_plan_says(string id, bool complete)
    {
        var inOrder = Oracle.InOrder(Pack.Case(id));
        Assert.That(inOrder.Complete, Is.EqualTo(complete));
        if (complete)
        {
            Assert.That(inOrder.Total, Is.EqualTo(Oracle.BestAssignment(Pack.Case(id)).Total));
        }
        else
        {
            Assert.That(inOrder.Parties[^1], Is.Null);
        }
    }

    [Test]
    public void A_best_party_passes_each_eval_rubric()
    {
        foreach (var c in Pack.Cases)
        {
            var best = Oracle.BestAssignment(c);
            var commits = c.Rung == "cover" && best.Total is not null ? [Oracle.Best(c.Quests[0]).Smallest[0]] : best.Parties;
            var result = Eval.Grade(c, commits, c.Rung == "prose" ? c.Anchor : null);
            Assert.That(result.Verdicts.Where(v => !v.Ok), Is.Empty, c.Id);
            Assert.That(result.Pass, Is.True, c.Id);
        }
    }

    [Test]
    public void A_dropped_hero_fails_valid_in_the_named_rubric()
    {
        var result = Eval.Grade(Pack.Case("b1"), [["cleric", "guard"]]);
        Assert.That(result.Pass, Is.False);
        Assert.That(result.Of("valid")!.Reason, Does.Contain("b1: fighter is false"));
    }

    [Test]
    public void An_unavailable_hero_fails_valid()
    {
        var result = Eval.Grade(Pack.Case("a1"), [["paladin", "cleric"]]);
        Assert.That(result.Of("valid")!.Reason, Is.EqualTo("a1: available is false"));
    }

    [Test]
    public void A_costly_hero_fails_valid_over_the_budget()
    {
        var result = Eval.Grade(Pack.Case("b1"), [["cleric", "guard", "squire", "paladin"]]);
        Assert.That(result.Of("valid")!.Reason, Does.Contain("b1: budget is false"));
    }

    [Test]
    public void An_extra_hero_within_the_budget_passes_but_fails_best_cost()
    {
        var result = Eval.Grade(Pack.Case("b1"), [["cleric", "guard", "squire", "hunter"]]);
        Assert.That(result.Pass, Is.True);
        Assert.That(result.Of("best-cost")!.Ok, Is.False);
        Assert.That(result.Measures["cost"], Is.EqualTo("8"));
        Assert.That(result.Measures["best cost"], Is.EqualTo("6"));
    }

    [Test]
    public void A_larger_cover_passes_but_fails_smallest()
    {
        var result = Eval.Grade(Pack.Case("c1"), [["cleric", "guard"]]);
        Assert.That(result.Pass, Is.True);
        Assert.That(result.Of("smallest")!.Ok, Is.False);
    }

    [Test]
    public void A_party_for_a_none_case_fails_valid()
    {
        Assert.That(Eval.Grade(Pack.Case("b4"), [["cleric", "guard", "thief", "apprentice"]]).Pass, Is.False);
        Assert.That(Eval.Grade(Pack.Case("b4"), [null]).Pass, Is.True);
    }

    [Test]
    public void None_for_a_case_with_a_valid_party_fails_valid()
    {
        Assert.That(Eval.Grade(Pack.Case("b1"), [null]).Pass, Is.False);
    }

    [Test]
    public void An_unknown_hero_fails_valid()
    {
        Assert.That(Eval.Grade(Pack.Case("c1"), [["paladin", "dragon"]]).Of("valid")!.Reason, Does.Contain("unknown or repeated hero"));
    }

    [Test]
    public void A_hero_in_two_parties_fails_the_pool_rubric()
    {
        var result = Eval.Grade(Pack.Case("m3"), [["cleric"], ["cleric"]]);
        Assert.That(result.Pass, Is.False);
        var m1 = Eval.Grade(Pack.Case("m1"), [["druid", "guard"], ["cleric", "guard"]]);
        Assert.That(m1.Of("valid")!.Reason, Does.Contain("m1: pool is false"));
    }

    [Test]
    public void The_in_order_baseline_fails_m1_in_the_eval()
    {
        var inOrder = Oracle.InOrder(Pack.Case("m1"));
        var result = Eval.Grade(Pack.Case("m1"), inOrder.Parties);
        Assert.That(result.Pass, Is.False);
        Assert.That(result.Of("valid")!.Reason, Does.Contain("m1-q2: none"));
    }

    [Test]
    public void The_spec_compare_counts_missed_extra_and_wrong_amounts()
    {
        var hidden = Pack.Case("p1").Anchor;
        var decomposed = Anchor.Parse("""
            ---
            id: p1
            kind: success
            level: case
            ---
            Orcs.

            - id: fighter
              ask: Does the party have a fighter?
              check: grader
              args: { tag: fighter }
            - id: strength
              ask: Is the total strength 10 or more?
              check: grader
              args: { stat: strength, min: 10 }
            - id: mage
              ask: Does the party have a mage?
              check: grader
              args: { tag: mage }
            - id: budget
              ask: Is the cost 12 or less?
              check: grader
              args: { max_cost: 12 }
            """, "decomposed");
        Assert.That(PartyEval.Compare(hidden, decomposed), Is.EqualTo((1, 1, 1)));
        var result = Eval.Grade(Pack.Case("p1"), [Oracle.Best(Pack.Case("p1").Quests[0]).Cheapest[0]], decomposed);
        Assert.That(result.Of("spec")!.Ok, Is.False);
        Assert.That(result.Measures["spec"], Is.EqualTo("1 missed, 1 extra, 1 wrong amount"));
    }

    private static string Join(IReadOnlyList<string> party) => string.Join(",", party);
}
