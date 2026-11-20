using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Providers;
using Orchestrator.Store;
using Orchestrator.Web;
using Fake = Orchestrator.Providers.Fake.FakeProvider;

namespace Orchestrator.Tests;

/// <summary>The demo control pane (page /demo): each action writes one event into a demo store with the fake provider.</summary>
[NonParallelizable]
public sealed class DemoPaneTests
{
    string folder = "";
    FileStore store = null!;
    Fake fake = null!;
    int ticks;

    [SetUp]
    public void Prepare()
    {
        folder = Directory.CreateTempSubdirectory("orch-pane-").FullName;
        store = new FileStore(Path.Combine(folder, "store"));
        var behavior = Path.Combine(folder, "fake.json");
        File.WriteAllText(behavior, """{ "turn": { "ok": true } }""");
        fake = new Fake(new ProviderOptions()) { BehaviorFile = behavior };
        ticks = 0;
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

    DemoPane Pane(IProvider? provider = null, int port = 7791) => new(store, provider ?? fake, port, () => ticks++);

    [Test]
    public void The_pane_refuses_the_real_provider_the_real_port_and_the_real_store()
    {
        Assert.That(Pane().Refusal, Is.Null);
        Assert.That(Pane(ProviderFactory.Create(new ProviderOptions(), null)).Refusal, Does.Contain("fake provider"));
        Assert.That(Pane(port: 7777).Refusal, Does.Contain("7777"));
        Assert.That(DemoPane.Refuse(Home.DefaultStoreRoot(), 7791, fake), Does.Contain("real store"));
        Assert.That(DemoPane.Refuse(Home.OldStoreRoot() + "\\", 7791, fake), Does.Contain("real store"));
        var refused = Pane(port: 7777);
        Assert.Throws<MessageException>(() => refused.AddQuestion(null));
        Assert.That(store.AllSessions(), Is.Empty, "a refused pane writes nothing");
    }

    [Test]
    public void Add_session_makes_a_root_or_a_child_with_a_brief()
    {
        var pane = Pane();
        pane.AddSession(null);
        var root = store.AllSessions().Single();
        pane.AddSession(root);
        var child = store.AllSessions().Single(id => id != root);
        Assert.That(store.Session(child).Parent, Is.EqualTo(root));
        Assert.That(store.Events.OfKind(EventKinds.Brief).Count, Is.EqualTo(2));
        Assert.That(ticks, Is.EqualTo(2), "each action starts a tick");
    }

    [Test]
    public void Add_question_and_add_answer_open_and_close_a_question()
    {
        var pane = Pane();
        pane.AddSession(null);
        var id = store.AllSessions().Single();
        pane.AddQuestion(id);
        var question = store.Events.OfKind(EventKinds.Question).Single();
        Assert.That((question.From, question.To, question.Needs), Is.EqualTo((Parties.Session(id), Parties.User, Needs.Answer)));
        Assert.That(store.OpenDecisions(), Has.Count.EqualTo(1));
        Assert.That(pane.AddAnswer(), Does.StartWith("Answer to"));
        Assert.That(store.OpenDecisions(), Is.Empty);
        Assert.That(pane.AddAnswer(), Is.EqualTo("No open question to answer."));
    }

    [Test]
    public void Add_report_update_and_message_post_the_kinds_of_the_plugin_tools()
    {
        var pane = Pane();
        pane.AddSession(null);
        var root = store.AllSessions().Single();
        pane.AddSession(root);
        var child = store.AllSessions().Single(id => id != root);
        pane.AddReport(child);
        pane.AddUpdate(child);
        pane.AddMessage(child);
        var report = store.Events.OfKind(EventKinds.Report).Single();
        Assert.That((report.To, report.Needs), Is.EqualTo((Parties.User, Needs.Ack)));
        Assert.That(store.Events.Fields(report)["state"], Is.AnyOf(ReportStates.Done, ReportStates.Blocked));
        Assert.That(store.Events.OfKind(EventKinds.Update).Single().From, Is.EqualTo(Parties.Session(child)));
        Assert.That(store.Events.OfKind(EventKinds.Message).Single().To, Is.EqualTo(Parties.Session(root)), "a child sends to its parent");
    }

    [Test]
    public async Task Fail_turns_writes_a_session_rule_into_the_behavior_file_and_removes_it_again()
    {
        var pane = Pane();
        pane.AddSession(null);
        var id = store.AllSessions().Single();
        pane.ToggleFailure(id);
        Assert.That(pane.Fails(id), Is.True);
        Assert.That((await fake.Turn(new TurnRequest(id, folder, "x", false), CancellationToken.None)).Ok, Is.False);
        pane.ToggleFailure(id);
        Assert.That(pane.Fails(id), Is.False);
        Assert.That((await fake.Turn(new TurnRequest(id, folder, "x", false), CancellationToken.None)).Ok, Is.True);
    }

    [Test]
    public void Burst_writes_the_number_of_events_and_starts_with_no_session()
    {
        var lines = Pane().Burst(10);
        Assert.That(lines, Has.Count.EqualTo(10));
        Assert.That(store.AllSessions(), Is.Not.Empty, "an action with no session makes one");
    }
}
