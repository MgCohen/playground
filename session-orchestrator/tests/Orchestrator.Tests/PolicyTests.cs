using Orchestrator.Supervisor;

namespace Orchestrator.Tests;

public sealed class PolicyTests
{
    [Test]
    public async Task A_medium_decision_gets_its_default_after_its_timeout_not_before_with_a_note_and_a_ledger_line()
    {
        using var t = new Rig();
        var id = await t.Started();
        t.Decide(id, "d1", "medium", "B");
        t.Clock.Advance(TimeSpan.FromSeconds(599));
        await t.Cycle();
        Assert.That(t.Store.IsAnswered(id, "d1"), Is.False);
        t.Clock.Advance(TimeSpan.FromSeconds(2));
        await t.Cycle();
        var answer = t.Store.Answer(id, "d1")!;
        Assert.That(answer.Value, Is.EqualTo("B"));
        Assert.That(answer.By, Is.EqualTo("policy"));
        Assert.That(answer.Note, Does.Contain("orch decision start d1"));
        Assert.That(t.Turns[^1].Prompt, Does.StartWith("Answer to question d1 (Q d1): B\n\nThis is a default decision"));
        var ledger = t.Store.Ledger();
        Assert.That(ledger[0].From, Is.EqualTo("policy"));
        Assert.That(ledger[0].Subject, Is.EqualTo("B"));
    }

    [Test]
    public async Task A_high_decision_and_a_decision_with_no_default_never_get_a_default()
    {
        using var t = new Rig();
        var id = await t.Started();
        t.Decide(id, "d1", "high", "A");
        t.Decide(id, "d2", "medium", null);
        t.Clock.Advance(TimeSpan.FromDays(10));
        await t.Cycle();
        Assert.That(t.Store.OpenDecisions(id), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task An_unknown_level_counts_as_the_strictest_level()
    {
        using var t = new Rig();
        var id = await t.Started();
        t.Decide(id, "d1", "tiny", "A");
        t.Clock.Advance(TimeSpan.FromDays(10));
        await t.Cycle();
        Assert.That(t.Store.IsAnswered(id, "d1"), Is.False);
    }

    [Test]
    public async Task A_low_decision_that_the_docs_answer_gets_that_answer_at_once_with_its_source()
    {
        using var t = new Rig(docs: new DocsResult(true, "A", "AGENTS.md, Rules"));
        var id = await t.Started();
        t.Decide(id, "d1", "low", "B");
        await t.Cycle();
        var answer = t.Store.Answer(id, "d1")!;
        Assert.That(new[] { answer.Value, answer.By, answer.Source }, Is.EqualTo(new[] { "A", "docs", "AGENTS.md, Rules" }));
        Assert.That(t.Docs.Checked, Is.EqualTo(new[] { "Q d1" }));
        await t.Cycle();
        Assert.That(t.Docs.Checked, Is.EqualTo(new[] { "Q d1" }));
        Assert.That(t.Turns[^1].Prompt, Does.Contain("comes from the documentation (AGENTS.md, Rules)"));
    }

    [Test]
    public async Task A_low_decision_that_the_docs_do_not_answer_waits_for_its_timeout()
    {
        using var t = new Rig();
        var id = await t.Started();
        t.Decide(id, "d1", "low", "B");
        await t.Cycle();
        Assert.That(t.Store.IsAnswered(id, "d1"), Is.False);
        t.Clock.Advance(TimeSpan.FromSeconds(61));
        await t.Cycle();
        Assert.That(t.Store.Answer(id, "d1")!.By, Is.EqualTo("policy"));
        Assert.That(t.Docs.Checked, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task A_medium_decision_does_not_check_the_docs()
    {
        using var t = new Rig(docs: new DocsResult(true, "A", "x"));
        var id = await t.Started();
        t.Decide(id, "d1", "medium", "B");
        await t.Cycle();
        Assert.That(t.Docs.Checked, Is.Empty);
    }

    [Test]
    public async Task A_review_sends_the_instruction_for_its_verdict_to_the_session()
    {
        using var t = new Rig();
        var id = await t.Started();
        t.Decide(id, "d1", "medium", "B");
        t.Supervisor.Review(id, "d1", "refuse", "use A");
        await t.Cycle();
        var prompt = t.Turns[^1].Prompt;
        Assert.That(prompt, Does.StartWith("Review of decision d1 (Q d1): refused. The user said: use A"));
        Assert.That(prompt, Does.Contain("each PR stacked above it"));
        Assert.That(t.Store.Review(id, "d1")!.Verdict, Is.EqualTo("refuse"));
    }
}
