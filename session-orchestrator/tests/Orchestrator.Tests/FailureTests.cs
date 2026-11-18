using Orchestrator.Core.Events;
using Orchestrator.Providers;
using Orchestrator.Store;
using Orchestrator.Supervisor;
using Fake = Orchestrator.Providers.Fake.FakeProvider;

namespace Orchestrator.Tests;

/// <summary>Failed sessions give one failure thread for each session and reason, not one card for each envelope and try (D, problem 4).</summary>
[NonParallelizable]
public sealed class FailureTests
{
    string folder = "";
    string behavior = "";
    ManualClock clock = null!;
    FileStore store = null!;
    Fake fake = null!;
    Supervisor.Supervisor supervisor = null!;

    [SetUp]
    public void Prepare()
    {
        folder = Directory.CreateTempSubdirectory("orch-failure-").FullName;
        behavior = Path.Combine(folder, "behavior.json");
        Behave("""{ "turn": { "ok": false, "error": "boom" } }""");
        clock = new ManualClock(DateTimeOffset.UtcNow);
        store = new FileStore(Path.Combine(folder, "store"), clock);
        fake = new Fake(new ProviderOptions()) { BehaviorFile = behavior };
        supervisor = new Supervisor.Supervisor(store, new ProviderTurns(fake), new FakeDocs(new DocsResult(false)),
            new SupervisorOptions(2, 3, 2, Rig.TestPolicy) { Provider = fake, RetryAfter = TimeSpan.FromMinutes(5) });
    }

    [TearDown]
    public void Clean()
    {
        try
        {
            Directory.Delete(folder, true);
        }
        catch (IOException)
        {
        }
    }

    void Behave(string json) => File.WriteAllText(behavior, json);

    async Task Cycles(int count)
    {
        for (var i = 0; i < count; i++)
        {
            supervisor.Tick();
            await supervisor.Idle();
        }
    }

    string Session(string title)
    {
        var id = store.Create(new Seed { Title = title, Cwd = folder, CreatedBy = "supervisor" }, "start");
        for (var i = 0; i < 3; i++)
            store.Events.Post(new PostInput(EventKinds.Message, Parties.User, Parties.Session(id), new Dictionary<string, object?> { ["text"] = $"more {i}" }) { Session = id });
        return id;
    }

    List<Card> Cards() => [.. new Inbox(store).Cards()];

    [Test]
    public async Task Four_failed_sessions_give_four_failure_cards_and_no_card_for_each_envelope()
    {
        var ids = Enumerable.Range(1, 4).Select(n => Session($"s{n}")).ToList();
        await Cycles(6);

        var failures = Cards().Where(c => c.Kind == EventKinds.Failure).ToList();
        Assert.That(failures.Select(c => c.Session), Is.EquivalentTo(ids), "one failure card for each session");
        Assert.That(Cards().Where(c => c.Kind is EventKinds.DeliveryBlocked or EventKinds.Blocked), Is.Empty, "no card for each envelope or try");
        var fields = failures[0].Fields;
        Assert.That((fields["reason"], fields["state"], fields["error"]), Is.EqualTo((Failures.FailedTurns, Failures.Open, "boom")));
        Assert.That(fields["tries"], Is.EqualTo(3.0));
        Assert.That((fields["envelopes"] as List<object?>)!.Count, Is.EqualTo(4), "the brief and the three messages wait");
        Assert.That(store.Events.OfKind(EventKinds.Blocked).Count(e => e.Session == ids[0]), Is.EqualTo(1), "the blocked state has no count, so it does not change with each try");
        Assert.That(store.Events.OfKind(EventKinds.DeliveryBlocked).Where(e => e.To == Parties.Supervisor), Is.Empty);
    }

    [Test]
    public async Task A_new_try_updates_the_card_and_a_recovery_closes_it()
    {
        var id = Session("flaky");
        await Cycles(4);
        var first = Cards().Single(c => c.Kind == EventKinds.Failure);
        Assert.That(first.Envelope.Thread, Is.EqualTo(Failures.Thread(id, Failures.FailedTurns)));

        clock.Advance(TimeSpan.FromMinutes(6));
        await Cycles(4);
        var second = Cards().Single(c => c.Kind == EventKinds.Failure);
        Assert.That(second.Envelope.Id, Is.Not.EqualTo(first.Envelope.Id), "a new try posts a new failure in the same thread");
        Assert.That(second.Fields["first"], Is.EqualTo(first.Fields["first"]));

        Behave("""{ "turn": { "ok": true } }""");
        clock.Advance(TimeSpan.FromMinutes(11));
        await Cycles(3);
        Assert.That(Cards().Where(c => c.Kind == EventKinds.Failure), Is.Empty, "the recovery closes the card");
        var last = store.Events.Thread(Failures.Thread(id, Failures.FailedTurns)).Last();
        Assert.That(store.Events.Fields(last)["state"], Is.EqualTo(Failures.Recovered));
        Assert.That(store.Undelivered(id), Is.Empty);
    }

    [Test]
    public async Task Seen_hides_the_card_until_the_failure_changes()
    {
        Session("down");
        await Cycles(4);
        var card = Cards().Single(c => c.Kind == EventKinds.Failure);
        new Inbox(store).Accept(card.Envelope.Id);
        Assert.That(Cards().Where(c => c.Kind == EventKinds.Failure), Is.Empty, "Seen is an ack");
        await Cycles(2);
        Assert.That(Cards().Where(c => c.Kind == EventKinds.Failure), Is.Empty, "the same facts post nothing");
        clock.Advance(TimeSpan.FromMinutes(6));
        await Cycles(4);
        Assert.That(Cards().Count(c => c.Kind == EventKinds.Failure), Is.EqualTo(1), "a new try shows the card again");
    }
}
