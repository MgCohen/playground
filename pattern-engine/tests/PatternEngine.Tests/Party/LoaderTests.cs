using PatternEngine.Domains.Party;
using PatternEngine.Features.Compose.Domain.Shared;
using PatternEngine.Plugins.Yaml;

namespace PatternEngine.Tests.Party;

public sealed class LoaderTests
{
    private static readonly PartyWords Words = new(["healer", "tank"], ["strength", "intelligence", "agility", "knowledge"], 1, 10);

    [Test]
    public void A_good_hero_file_parses()
    {
        var (hero, errors) = HeroCatalog.Parse("---\nid: cleric\ntags: [healer]\ncost: 3\nstats: { strength: 3, intelligence: 5, agility: 2, knowledge: 6 }\n---\nA priest.\n", Words);
        Assert.That(errors, Is.Empty);
        Assert.That(hero!.Cost, Is.EqualTo(3));
        Assert.That(hero.Stat("knowledge"), Is.EqualTo(6));
    }

    [TestCase("tags: [necromancer]", "unknown tag \"necromancer\"")]
    [TestCase("tags: []", "no tags")]
    public void A_bad_tag_is_an_error(string tags, string error)
    {
        var (hero, errors) = HeroCatalog.Parse($"---\nid: x\n{tags}\ncost: 3\nstats: {{ strength: 3, intelligence: 5, agility: 2, knowledge: 6 }}\n---\n", Words);
        Assert.That(hero, Is.Null);
        Assert.That(errors, Does.Contain(error));
    }

    [TestCase("cost: -1\nstats: { strength: 3, intelligence: 5, agility: 2, knowledge: 6 }", "cost must be an integer, 0 or more")]
    [TestCase("cost: 1\nstats: { strength: 11, intelligence: 5, agility: 2, knowledge: 6 }", "stat \"strength\" must be an integer from 1 to 10")]
    [TestCase("cost: 1\nstats: { luck: 3, strength: 3, intelligence: 5, agility: 2, knowledge: 6 }", "unknown stat \"luck\"")]
    [TestCase("cost: 1\nstats: { strength: 3, intelligence: 5, agility: 2 }", "no stat \"knowledge\"")]
    public void A_bad_cost_or_stat_is_an_error(string body, string error)
    {
        var (_, errors) = HeroCatalog.Parse($"---\nid: x\ntags: [tank]\n{body}\n---\n", Words);
        Assert.That(errors, Does.Contain(error));
    }

    [Test]
    public void An_anchor_parses_its_context_and_rubrics()
    {
        var a = Anchor.Parse("---\nid: q\nkind: success\nlevel: case\n---\nHeal.\n\n- id: healer\n  ask: Does the party have a healer?\n  check: grader\n  args: { tag: healer }\n  visibility: eval\n", "q.md");
        Assert.That(a.Errors, Is.Empty);
        Assert.That(a.Context, Is.EqualTo("Heal."));
        Assert.That(a.Rubrics.Single().Hidden, Is.True);
        Assert.That(a.Visible, Is.Empty);
    }

    [Test]
    public void The_domain_fields_are_in_their_own_block()
    {
        var a = Anchor.Parse("---\nid: m\nkind: success\nlevel: case\ndomain: { rung: many, quests: [q1, q2] }\n---\nTwo.\n", "m.md");
        Assert.That(a.Domain.Get("rung"), Is.EqualTo("many"));
        Assert.That(PatternEngine.Plugins.Yaml.Node.Strings(a.Domain.Raw("quests")), Is.EqualTo(new[] { "q1", "q2" }));
        Assert.That(a.Field("rung"), Is.Null);
    }

    [Test]
    public void A_bad_anchor_has_errors()
    {
        var a = Anchor.Parse("---\nid: q\nkind: wish\n---\n- id: a\n  ask: A?\n  check: grader\n- id: a\n  ask: B?\n  check: grader\n  visibility: secret\n", "q.md");
        Assert.That(a.Errors, Does.Contain("kind must be one of intent, constraints, success, review"));
        Assert.That(a.Errors, Does.Contain("rubric \"a\" is in the anchor more than once"));
        Assert.That(a.Errors, Does.Contain("rubric \"a\": visibility must be one of run, eval"));
    }

    [Test]
    public void Bad_rubric_args_are_errors()
    {
        var heroes = new HeroCatalog([], []);
        var a = Anchor.Parse("---\nid: q\nkind: success\n---\n- id: a\n  ask: A?\n  check: grader\n  args: { stat: luck, colour: red }\n- id: b\n  ask: B?\n  check: grader\n  args: { unavailable: [dragon] }\n", "q.md");
        var errors = PartyPack.Check(a, heroes, Words).ToList();
        Assert.That(errors, Does.Contain("rubric \"a\": unknown arg \"colour\""));
        Assert.That(errors, Does.Contain("rubric \"a\": unknown stat \"luck\""));
        Assert.That(errors, Does.Contain("rubric \"a\": stat and min go together"));
        Assert.That(errors, Does.Contain("rubric \"b\": unknown hero \"dragon\""));
    }
}
