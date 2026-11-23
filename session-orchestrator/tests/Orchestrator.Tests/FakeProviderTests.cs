using Orchestrator.Core.Events;
using Orchestrator.Providers;
using Fake = Orchestrator.Providers.Fake.FakeProvider;
using Orchestrator.Store;
using Orchestrator.Supervisor;

namespace Orchestrator.Tests;

/// <summary>The fake provider: a supervisor runs on dummy data with no model and no app (D, step 3).</summary>
[NonParallelizable]
public sealed class FakeProviderTests
{
    string folder = "";

    [SetUp]
    public void Prepare() => folder = Directory.CreateTempSubdirectory("orch-fake-").FullName;

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

    string Behavior(string json)
    {
        var path = Path.Combine(folder, "behavior.json");
        File.WriteAllText(path, json);
        return path;
    }

    static TurnRequest Request(string id) => new(id, "C:/x", "hello", false);

    [Test]
    public void The_factory_selects_a_provider_by_its_name()
    {
        Assert.That(ProviderFactory.Create(new ProviderOptions(), "fake"), Is.InstanceOf<Fake>());
        Assert.That(ProviderFactory.Create(new ProviderOptions(), "FAKE").Name, Is.EqualTo("Fake"));
        Assert.That(ProviderFactory.Create(new ProviderOptions(), null), Is.Not.InstanceOf<Fake>(), "no name: the default provider");
        var error = Assert.Throws<InvalidOperationException>(() => ProviderFactory.Create(new ProviderOptions(), "nope"))!;
        Assert.That(error.Message, Does.Contain("fake"));
    }

    [Test]
    public async Task With_no_behavior_file_each_turn_is_good_and_each_model_call_has_no_answer()
    {
        var fake = new Fake(new ProviderOptions()) { BehaviorFile = null };
        var turn = await fake.Turn(Request("s1"), CancellationToken.None);
        Assert.That((turn.Ok, turn.Text), Is.EqualTo((true, "fake turn of s1")));
        Assert.That((await fake.Ask(new AskRequest("C:/x", "q", 1), CancellationToken.None)).Ok, Is.False);
        Assert.That(fake.Read(fake.AppId("s1")), Is.EqualTo(new AppFacts(false, false)));
        Assert.That(fake.Turns.Single().Id, Is.EqualTo("s1"));
    }

    [Test]
    public async Task The_behavior_file_sets_the_result_of_each_session_and_a_change_applies_at_the_next_call()
    {
        var path = Behavior("""
            { "turn": { "text": "all good" },
              "sessions": { "bad": { "ok": false, "error": "boom" }, "bad-but": { "ok": true, "text": "longest prefix wins" } },
              "ask": { "text": "{ \"answered\": true, \"answer\": \"A\", \"source\": \"AGENTS.md\" }" },
              "app": { "inApp": true } }
            """);
        var fake = new Fake(new ProviderOptions()) { BehaviorFile = path };
        Assert.That((await fake.Turn(Request("good-1"), CancellationToken.None)).Text, Is.EqualTo("all good"));
        var bad = await fake.Turn(Request("bad-1"), CancellationToken.None);
        Assert.That((bad.Ok, bad.Error), Is.EqualTo((false, "boom")));
        Assert.That((await fake.Turn(Request("bad-but-ok"), CancellationToken.None)).Text, Is.EqualTo("longest prefix wins"));
        Assert.That(DocsChecker.ParseReply((await fake.Ask(new AskRequest("C:/x", "q", 1), CancellationToken.None)).Text!).Answer, Is.EqualTo("A"));
        Assert.That(fake.Read("x").InApp, Is.True);

        File.WriteAllText(path, """{ "turn": { "ok": false, "error": "now down" } }""");
        Assert.That((await fake.Turn(Request("good-1"), CancellationToken.None)).Error, Is.EqualTo("now down"));
        File.WriteAllText(path, "{ not json");
        Assert.That((await fake.Turn(Request("good-1"), CancellationToken.None)).Error, Does.Contain("not valid"));
    }

    [Test]
    public async Task A_supervisor_on_the_fake_provider_delivers_with_no_model()
    {
        var fake = new Fake(new ProviderOptions()) { BehaviorFile = Behavior("""{ "sessions": { } }""") };
        var store = new FileStore(Path.Combine(folder, "store"));
        var supervisor = new Supervisor.Supervisor(store, new ProviderTurns(fake), new FakeDocs(new DocsResult(false)), new SupervisorOptions(2, 2, 2, Rig.TestPolicy) { Provider = fake });
        var id = store.Create(new Seed { Title = "demo", Cwd = folder, CreatedBy = "supervisor" }, "Write hello.txt.");
        supervisor.Tick();
        await supervisor.Idle();
        Assert.That(fake.Turns.Single().Prompt, Does.Contain("Write hello.txt."));
        Assert.That(store.Undelivered(id), Is.Empty, "the good fake turn acknowledged the brief");
        Assert.That(store.Events.OfKind(EventKinds.TurnEnd).Count, Is.EqualTo(1));
    }
}
