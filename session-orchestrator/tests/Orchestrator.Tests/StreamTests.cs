using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Store;

namespace Orchestrator.Tests;

public sealed class StreamTests
{
    static Dictionary<string, object?> Body(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    [Test]
    public void Post_writes_the_body_first_and_the_envelope_points_to_it()
    {
        using var t = new Rig();
        var id = t.Create("start");
        var e = t.Store.Send(id, "hello");
        Assert.That(File.Exists(Path.Combine(t.Folder, e.Body)), Is.True);
        Assert.That(t.Store.Events.Find(e.Id)!.Body, Is.EqualTo(e.Body));
        Assert.That(t.Store.Events.Fields(e)["text"], Is.EqualTo("hello"));
    }

    [Test]
    public void Post_refuses_a_body_that_fails_the_schema_and_names_the_field()
    {
        using var t = new Rig();
        var id = t.Create("start");
        var refused = Assert.Throws<MessageException>(() => t.Store.Events.Post(new PostInput(EventKinds.Decision, Parties.Session(id), Parties.User, Body(("question", "Q?")))))!;
        Assert.That(refused.Message, Does.Contain("decision").And.Contain("required").IgnoreCase.And.Contain("context"));
    }

    [Test]
    public void Post_refuses_a_sender_or_a_receiver_that_meta_json_does_not_allow()
    {
        using var t = new Rig();
        var id = t.Create("start");
        var from = Assert.Throws<MessageException>(() => t.Store.Events.Post(new PostInput(EventKinds.Review, Parties.Session(id), Parties.Session(id), Body(("decision", "d"), ("verdict", "approve")))))!;
        Assert.That(from.Message, Does.Contain("from session:"));
        var to = Assert.Throws<MessageException>(() => t.Store.Events.Post(new PostInput(EventKinds.Notice, Parties.Supervisor, Parties.User, Body(("kind", "restart")))))!;
        Assert.That(to.Message, Does.Contain("to user"));
    }

    [Test]
    public void Post_refuses_a_reply_type_that_the_replied_type_does_not_take()
    {
        using var t = new Rig();
        var id = t.Create("start");
        var decision = t.Store.SaveDecision(new Decision("d1", id) { Risk = "high" });
        var refused = Assert.Throws<MessageException>(() => t.Store.Events.Post(new PostInput(EventKinds.Message, Parties.User, Parties.Session(id), Body(("text", "x")))
        {
            Refs = Body((RefKeys.Reply, decision.Id)),
        }))!;
        Assert.That(refused.Message, Does.Contain("takes only these replies: answer"));
    }

    [Test]
    public void A_second_answer_in_one_thread_is_refused_with_the_first_answer()
    {
        using var t = new Rig();
        var id = t.Create("start");
        t.Decide(id, "d1", "low", "A");
        new Messages(t.Store).Answer(id, "d1", "A", Parties.Policy, source: new AnswerSource(Parties.Policy) { Reason = "timeout" });
        var refused = Assert.Throws<MessageException>(() => new Messages(t.Store).Answer(id, "d1", "B"))!;
        Assert.That(refused.Message, Does.Contain("already has an answer from policy: A"));
    }

    [Test]
    public void A_forward_keeps_the_body_and_names_the_original()
    {
        using var t = new Rig();
        var id = t.Create("start");
        var decision = t.Store.SaveDecision(new Decision("d1", id) { Risk = "high" });
        var forward = t.Store.Events.Forward(decision, Parties.Session("parent"));
        Assert.That((forward.Body, forward.Of, forward.Thread, forward.From), Is.EqualTo((decision.Body, decision.Id, "d1", decision.From)));
        Assert.That(t.Store.Events.ForwardsOf(decision.Id).Single().Id, Is.EqualTo(forward.Id));
        Assert.That(t.Store.Decisions(id), Has.Count.EqualTo(1));
    }

    [Test]
    public void A_subscription_gets_a_late_envelope_of_another_writer_and_each_envelope_once()
    {
        using var t = new Rig();
        var subscription = t.Store.Events.Subscribe(Parties.Supervisor, "test", e => e.Kind == EventKinds.Message);
        var id = t.Create("first");
        var first = subscription.Next();
        Assert.That(first, Has.Count.EqualTo(1));
        subscription.Done(first[0].Id);
        var ms = long.Parse(first[0].Id.Split('-')[0]) - 1;
        var late = $"{ms}-0000late-live";
        var day = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd");
        Files.Write(Path.Combine(t.Folder, StoreFiles.Bodies, day, $"{late}.json"), """{ "text": "late" }""");
        Files.Write(Path.Combine(t.Folder, StoreFiles.Events, day, $"{late}.json"),
            $$"""{ "id": "{{late}}", "at": "x", "session": "{{id}}", "kind": "message", "from": "user", "to": "session:{{id}}", "subject": "late", "body": "bodies/{{day}}/{{late}}.json", "refs": {} }""");
        Assert.That(subscription.Next().Select(e => e.Id), Is.EqualTo(new[] { late }));
        subscription.Done(late);
        Assert.That(subscription.Next(), Is.Empty);
    }

    [Test]
    public void An_acked_envelope_is_not_pending_and_the_shared_cursor_moves_over_it()
    {
        using var t = new Rig();
        var id = t.Create("one");
        var second = t.Store.Send(id, "two");
        var pending = t.Store.Pending(id);
        Assert.That(pending.Envelopes, Has.Count.EqualTo(2));
        t.Store.MarkSent(id, pending.Envelopes, Holders.Live);
        Assert.That(t.Store.Pending(id).IsEmpty, Is.True);
        Assert.That(t.Store.Deliveries(id).Cursor, Is.EqualTo(second.Id));
        Assert.That(t.Store.Events.AcksOf(second.Id).Single().To, Is.EqualTo(Parties.User));
    }

    [Test]
    public void An_update_to_the_user_and_a_working_report_are_not_deliveries()
    {
        using var t = new Rig();
        var id = t.Create("start");
        t.Store.MarkSent(id, t.Store.Pending(id).Envelopes, Holders.Supervisor);
        t.Store.SaveReport(id, ReportStates.Working, "half");
        Assert.That(t.Store.Pending(id).IsEmpty, Is.True);
        Assert.That(t.Store.Report(id)!.Summary, Is.EqualTo("half"));
    }

    [Test]
    public void A_nudge_writes_the_nudge_file_of_the_party()
    {
        using var t = new Rig();
        t.Store.Events.Nudge(Parties.Session("abc"), "e1");
        Assert.That(File.ReadAllText(Path.Combine(t.Folder, StoreFiles.Nudges, "session-abc")), Is.EqualTo("e1"));
        Assert.That(t.Store.Events.Nudged(Parties.Session("abc")), Is.Not.Null);
    }

    [Test]
    public void The_render_of_a_policy_answer_has_the_note_and_a_message_of_a_session_names_its_sender()
    {
        using var t = new Rig();
        var id = t.Create("start", "child");
        t.Decide(id, "d1", "low", "A");
        var answer = new Messages(t.Store).Answer(id, "d1", "A", Parties.Policy, source: new AnswerSource(Parties.Policy) { Reason = "level low", OnDefault = "commit" });
        var text = t.Store.Renderer.Text(answer);
        Assert.That(text, Does.StartWith("Answer to decision d1 (Q d1): A"));
        Assert.That(text, Does.Contain("This is a default decision (level low)").And.Contain("its own commit"));
        Assert.That(t.Store.Renderer.Text(answer, "tool"), Does.StartWith("ANSWER: A"));
        var other = t.Create("start", "other");
        var message = t.Store.Send(other, "hi", Parties.Session(id));
        Assert.That(t.Store.Renderer.Text(message), Is.EqualTo($"Message from session \"child\" ({id}): hi"));
    }

    [Test]
    public void The_inbox_shows_a_decision_until_the_answer_and_then_the_default_for_review()
    {
        using var t = new Rig();
        var id = t.Create("start", "child");
        var decision = t.Store.SaveDecision(new Decision("d1", id) { Risk = "low", DefaultOption = "A" });
        var inbox = new Inbox(t.Store);
        Assert.That(inbox.Cards().Single().Envelope.Id, Is.EqualTo(decision.Id));
        new Messages(t.Store).Answer(id, "d1", "A", Parties.Policy, source: new AnswerSource(Parties.Policy) { Reason = "timeout" });
        var card = inbox.Cards().Single();
        Assert.That((card.Kind, card.Needs, card.Envelope.Thread), Is.EqualTo((EventKinds.Answer, Needs.Review, "d1")));
        inbox.Accept(card.Envelope.Id);
        Assert.That(inbox.Cards(), Is.Empty);
        Assert.That(t.Store.Review(id, "d1")!.Verdict, Is.EqualTo(Verdicts.Approve));
    }

    [Test]
    public void Accept_of_a_decision_card_answers_with_the_default_and_refuse_answers_with_the_text()
    {
        using var t = new Rig();
        var id = t.Create("start");
        var one = t.Store.SaveDecision(new Decision("d1", id) { Risk = "high", DefaultOption = "A" });
        var two = t.Store.SaveDecision(new Decision("d2", id) { Risk = "high" });
        var inbox = new Inbox(t.Store);
        inbox.Accept(one.Id);
        Assert.That(t.Store.Answer(id, "d1")!.Value, Is.EqualTo("A"));
        Assert.Throws<MessageException>(() => inbox.Accept(two.Id));
        inbox.Refuse(two.Id, "use C");
        Assert.That(t.Store.Answer(id, "d2")!.Value, Is.EqualTo("use C"));
    }

    [Test]
    public async Task A_root_report_is_a_card_until_the_ack_and_a_child_report_closes_when_the_parent_gets_it()
    {
        using var t = new Rig();
        var root = t.Create("r", "root");
        var child = t.Create("c", "child", root, "spawn_child");
        await t.Cycle();
        var rootReport = t.Store.SaveReport(root, ReportStates.Done, "root done");
        var childReport = t.Store.SaveReport(child, ReportStates.Done, "child done");
        var inbox = new Inbox(t.Store);
        Assert.That(inbox.Cards().Select(c => c.Envelope.Id), Is.EquivalentTo(new[] { rootReport.Id, childReport.Id }));
        await t.Cycle();
        Assert.That(inbox.Cards().Select(c => c.Envelope.Id), Is.EqualTo(new[] { rootReport.Id }));
        inbox.Accept(rootReport.Id);
        Assert.That(inbox.Cards(), Is.Empty);
    }

    [Test]
    public async Task The_watchdog_nudges_a_live_idle_session_and_then_posts_delivery_blocked()
    {
        using var t = new Rig();
        var id = t.Create("start");
        t.MakeLive(id, TimeSpan.Zero, TimeSpan.Zero);
        t.Store.WriteJson(id, new Status("idle", null, Times.Format(t.Clock.Now)), StoreFiles.Status);
        var message = t.Store.Send(id, "ping");
        await t.Cycle();
        Assert.That(t.Store.Events.Nudged(Parties.Session(id)), Is.Null);
        t.Clock.Advance(TimeSpan.FromSeconds(16));
        t.MakeLive(id, TimeSpan.Zero, TimeSpan.Zero);
        await t.Cycle();
        Assert.That(File.ReadAllText(Path.Combine(t.Folder, StoreFiles.Nudges, Parties.Folder(Parties.Session(id)))), Is.Not.Empty);
        t.Clock.Advance(TimeSpan.FromSeconds(60));
        t.MakeLive(id, TimeSpan.Zero, TimeSpan.Zero);
        await t.Cycle();
        var blocked = t.Store.Events.All().Where(e => e.Kind == EventKinds.DeliveryBlocked).ToList();
        Assert.That(blocked.Select(b => b.Ref("envelope")).Distinct(), Is.EquivalentTo(new[] { message.Id, t.Store.Events.All().First(e => e.Kind == EventKinds.Message).Id }));
        Assert.That(blocked.All(b => b.To == Parties.User && b.Needs == Needs.Ack), Is.True);
        Assert.That(new Inbox(t.Store).Cards().Count(c => c.Kind == EventKinds.DeliveryBlocked), Is.EqualTo(2));
        Assert.That(t.Turns, Is.Empty);
    }

    [Test]
    public async Task The_watchdog_waits_while_the_live_session_runs_a_turn()
    {
        using var t = new Rig();
        var id = t.Create("start");
        t.Store.WriteJson(id, new Status("running", "turn", Times.Format(t.Clock.Now)), StoreFiles.Status);
        t.Clock.Advance(TimeSpan.FromMinutes(5));
        t.MakeLive(id, TimeSpan.Zero, TimeSpan.Zero);
        await t.Cycle();
        Assert.That(t.Store.Events.All().Any(e => e.Kind == EventKinds.DeliveryBlocked), Is.False);
    }

    [Test]
    public void A_store_of_version_1_is_refused_with_no_migration()
    {
        using var t = new Rig();
        Files.WriteJson(Path.Combine(t.Folder, StoreFiles.Version), new StoreVersion(1, "x"));
        var refused = Assert.Throws<InvalidDataException>(() => Home.CheckVersion(t.Folder, t.Clock.Now))!;
        Assert.That(refused.Message, Does.Contain("no migration"));
    }

    [Test]
    public void Watch_lists_the_envelopes_with_no_ack_with_their_text_and_a_timeout_ends_with_1()
    {
        using var t = new Rig();
        var id = t.Create("first", "child");
        t.Decide(id, "d1", "high", null);
        Environment.SetEnvironmentVariable("ORCH_STORE", t.Folder);
        try
        {
            var output = new StringWriter();
            Assert.That(Cli.Cli.Run(["watch", "--to", $"session:{id}", "--list"], output, output, t.Folder), Is.EqualTo(0));
            Assert.That(output.ToString(), Does.Contain("\"text\":\"first\"").And.Contain("\"kind\":\"message\""));
            var none = new StringWriter();
            Assert.That(Cli.Cli.Run(["watch", "--to", $"session:{id}", "--kind", "answer", "--thread", "d1", "--once", "--timeout", "0.2"], none, none, t.Folder), Is.EqualTo(1));
            new Messages(t.Store).Answer(id, "d1", "GREEN");
            var answer = new StringWriter();
            Assert.That(Cli.Cli.Run(["watch", "--to", $"session:{id}", "--kind", "answer", "--thread", "d1", "--once", "--timeout", "5", "--render", "tool"], answer, answer, t.Folder), Is.EqualTo(0));
            Assert.That(answer.ToString(), Does.Contain("\"text\":\"ANSWER: GREEN\""));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORCH_STORE", null);
        }
    }

    [Test]
    public void A_chat_line_answers_only_when_one_decision_is_open_and_the_answer_has_an_ack()
    {
        using var t = new Rig();
        var id = t.Create("first");
        t.Decide(id, "d1", "high", null);
        t.Decide(id, "d2", "high", null);
        Environment.SetEnvironmentVariable("ORCH_STORE", t.Folder);
        try
        {
            var output = new StringWriter();
            Assert.That(Cli.Cli.Run(["chat-answer", id, "use", "B"], output, output, t.Folder), Is.EqualTo(0));
            Assert.That(t.Store.OpenDecisions(id), Has.Count.EqualTo(2));
            new Messages(t.Store).Answer(id, "d1", "A");
            Assert.That(Cli.Cli.Run(["chat-answer", id, "use", "B"], output, output, t.Store.Root), Is.EqualTo(0));
            var answer = t.Store.Events.FirstAnswer("d2")!;
            Assert.That((t.Store.Events.Fields(answer)["text"], answer.Via), Is.EqualTo(("use B", "chat")));
            Assert.That(t.Store.Events.IsAcked(answer.Id), Is.True);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORCH_STORE", null);
        }
    }
}
