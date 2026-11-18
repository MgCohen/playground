using AngleSharp.Html.Parser;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Store;
using Orchestrator.Web;
using Orchestrator.Web.Components.Pages;

namespace Orchestrator.Tests;

/// <summary>The inbox UI of A4: card types, the pile, skip and undo, home, stream, sessions and the full document.</summary>
public sealed class InboxUiTests
{
    static BunitContext Context(Rig rig)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new Board(rig.Store, rig.Supervisor));
        context.Services.AddSingleton(new StreamHub(rig.Store));
        return context;
    }

    static Event Ask(Rig rig, string session, string id, string question, string[] options, string? defaultOption) =>
        rig.Store.SaveDecision(new Decision(id, session) { Question = question, Options = [.. options], DefaultOption = defaultOption, Risk = "medium" });

    static Event Query(Rig rig, string session, string question) =>
        rig.Store.Events.Post(new PostInput(EventKinds.Question, Parties.Session(session), Parties.User, new Dictionary<string, object?> { ["question"] = question, ["context"] = "for the test" })
        { Session = session, Thread = "q-" + question.GetHashCode().ToString("x"), Needs = Needs.Answer });

    static string Session(Rig rig, string title, string? parent = null) =>
        rig.Store.Create(new Meta("") { Title = title, Cwd = "C:/x", Parent = parent }, "start");

    [Test]
    public void Each_card_type_has_its_own_actions()
    {
        using var rig = new Rig();
        var s = Session(rig, "Types");
        Ask(rig, s, "t-yes", "May I keep the old routes?", ["yes", "no"], "yes");
        Ask(rig, s, "t-def", "Which sweep interval?", ["60 s", "30 s"], "60 s");
        Ask(rig, s, "t-op", "Which color?", ["RED", "BLUE"], null);
        Query(rig, s, "Which store?");
        rig.Store.SaveReport(s, ReportStates.Done, "All done");
        rig.Store.SaveReport(Session(rig, "Blocked one"), ReportStates.Blocked, "Cannot start");

        var cards = new Board(rig.Store, rig.Supervisor).Cards().ToDictionary(c => c.Title, c => (c.Type, c.Right.Label, c.Left.Label, c.Group));
        Assert.That(cards["May I keep the old routes?"], Is.EqualTo(("yesno", "Yes", "No", CardGroup.Questions)));
        Assert.That(cards["Which sweep interval?"], Is.EqualTo(("default", "Accept", "Other", CardGroup.Questions)));
        Assert.That(cards["Which color?"], Is.EqualTo(("opinion", "Speak", "Type", CardGroup.Questions)));
        Assert.That(cards["Which store?"], Is.EqualTo(("question", "Speak", "Type", CardGroup.Questions)));
        Assert.That(cards["All done"], Is.EqualTo(("report", "Got it", "Reply", CardGroup.Reports)));
        Assert.That(cards["Cannot start"], Is.EqualTo(("blocked", "Seen", "Reply", CardGroup.Blocked)));
    }

    [Test]
    public void A_question_with_a_risk_or_a_default_has_the_label_decision()
    {
        Assert.That(Ui.KindLabel(EventKinds.Question, new Dictionary<string, object?> { ["question"] = "Q", ["risk"] = "low" }), Is.EqualTo("Decision"));
        Assert.That(Ui.KindLabel(EventKinds.Question, new Dictionary<string, object?> { ["question"] = "Q", ["defaultOption"] = "A" }), Is.EqualTo("Decision"));
        Assert.That(Ui.KindLabel(EventKinds.Question, new Dictionary<string, object?> { ["question"] = "Q" }), Is.EqualTo("Question"));
        Assert.That(Ui.KindLabel(EventKinds.Question), Is.EqualTo("Question"));
    }

    [Test]
    public async Task A_supervisor_with_no_ticks_starts_no_turn()
    {
        using var rig = new Rig();
        var id = rig.Create("start");
        var view = new Orchestrator.Supervisor.Supervisor(rig.Store, rig.Runner, rig.Docs, rig.Supervisor.Options with { Ticks = false });
        view.Tick();
        await view.Idle();
        Assert.That(rig.Turns, Is.Empty);
        await rig.Cycle();
        Assert.That(rig.Turns.Select(t => t.Id), Is.EqualTo(new[] { id }));
    }

    [Test]
    public void The_pile_is_in_arrival_order_and_skip_moves_the_top_card_to_the_end()
    {
        using var rig = new Rig();
        var s = Session(rig, "Pile");
        Ask(rig, s, "p1", "First question?", ["A", "B"], "A");
        Ask(rig, s, "p2", "Second question?", ["A", "B"], "B");
        using var context = Context(rig);
        var page = context.Render<Cards>();
        Assert.That(page.Find(".pcard .q").TextContent, Is.EqualTo("First question?"));
        Assert.That(page.Find("[data-act=right]").TextContent, Is.EqualTo("Accept"));

        page.Find("[data-act=skip]").Click();
        Assert.That(page.Find(".pcard .q").TextContent, Is.EqualTo("Second question?"));
        Assert.That(page.Find(".toast").TextContent, Does.Contain("end of the pile"));

        Ask(rig, s, "p3", "Third question?", ["A", "B"], "A");
        page.Find("[data-act=skip]").Click();
        Assert.That(page.Find(".pcard .q").TextContent, Is.EqualTo("First question?"), "the skipped card comes before a card that arrived after the skip");
    }

    [Test]
    public void An_act_waits_for_the_undo_time_and_undo_cancels_it()
    {
        using var rig = new Rig();
        var s = Session(rig, "Undo");
        Ask(rig, s, "u1", "Undo question?", ["A", "B"], "A");
        using var context = Context(rig);
        var page = context.Render<Cards>();

        page.Find("[data-act=right]").Click();
        Assert.That(page.FindAll(".pcard"), Is.Empty, "the card leaves the pile at once");
        Assert.That(rig.Store.Events.FirstAnswer("u1"), Is.Null, "the answer waits for the undo time");
        page.Find(".toast button").Click();
        Assert.That(page.Find(".pcard .q").TextContent, Is.EqualTo("Undo question?"));

        page.Find("[data-act=right]").Click();
        context.Dispose();
        Assert.That(rig.Store.Fields(rig.Store.Events.FirstAnswer("u1")!)["text"], Is.EqualTo("A"), "a view that closes sends the waiting act");
    }

    [Test]
    public void The_answer_sheet_sends_the_text_and_has_a_mic()
    {
        using var rig = new Rig();
        var s = Session(rig, "Sheet");
        Ask(rig, s, "o1", "Which color?", ["RED", "BLUE"], null);
        using var context = Context(rig);
        var page = context.Render<Cards>();
        page.Find("[data-act=right]").Click();
        Assert.That(page.Find("[data-speech-for=sheet-text]"), Is.Not.Null);
        page.Find("#sheet-text").Input("GREEN");
        page.Find(".sheet .btn.pri").Click();
        context.Dispose();
        Assert.That(rig.Store.Fields(rig.Store.Events.FirstAnswer("o1")!)["text"], Is.EqualTo("GREEN"));
    }

    [Test]
    public void The_reports_chip_shows_reports_and_blocked_cards_only()
    {
        using var rig = new Rig();
        var s = Session(rig, "Chips");
        Ask(rig, s, "c1", "A question?", ["A", "B"], "A");
        rig.Store.SaveReport(s, ReportStates.Blocked, "Blocked now");
        using var context = Context(rig);
        var page = context.Render<Cards>();
        page.FindAll(".seg button").Single(b => b.TextContent.StartsWith("Reports")).Click();
        Assert.That(page.Find(".pcard .q").TextContent, Is.EqualTo("Blocked now"));
        Assert.That(page.Find("[data-act=right]").TextContent, Is.EqualTo("Seen"));
    }

    [Test]
    public void Home_shows_the_waiting_cards_by_type_and_an_empty_state()
    {
        using var rig = new Rig();
        using (var context = Context(rig))
        {
            var empty = context.Render<HomePage>();
            Assert.That(empty.Markup, Does.Contain("Nothing waits for you"));
            Assert.That(empty.Markup, Does.Contain("Nothing waiting"));
        }
        var s = Session(rig, "Home child");
        Ask(rig, s, "h1", "Home question?", ["A", "B"], "A");
        rig.Store.SaveReport(s, ReportStates.Done, "Home report");
        using var context2 = Context(rig);
        var page = context2.Render<HomePage>();
        Assert.That(page.Find(".ring-n").TextContent, Is.EqualTo("2"));
        var legend = page.FindAll(".lgr").ToDictionary(r => r.QuerySelector(".grow")!.TextContent, r => r.QuerySelector("b")!.TextContent);
        Assert.That(legend["Questions"], Is.EqualTo("1"));
        Assert.That(legend["Reports"], Is.EqualTo("1"));
        Assert.That(page.Markup, Does.Contain("Home question?"));
        Assert.That(page.Markup, Does.Contain("Home child"));
    }

    [Test]
    public void The_session_stream_is_the_stream_locked_to_the_session_and_its_children()
    {
        using var rig = new Rig();
        var a = Session(rig, "Root A");
        var child = Session(rig, "Child of A", a);
        var b = Session(rig, "Root B");
        Ask(rig, child, "s1", "Question of the child?", ["A", "B"], "A");
        Ask(rig, b, "s2", "Question of B?", ["A", "B"], "A");
        using var context = Context(rig);
        var all = context.Render<StreamPage>();
        Assert.That(all.Markup, Does.Contain("Question of the child?").And.Contain("Question of B?"));
        var nav = context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo($"stream?session={a}");
        var locked = context.Render<StreamPage>();
        Assert.That(locked.Markup, Does.Contain("Question of the child?"));
        Assert.That(locked.Markup, Does.Not.Contain("Question of B?"));
        Assert.That(locked.Find(".lock").TextContent, Does.Contain("Root A").And.Contain("1 children"));
    }

    [Test]
    public void A_closed_item_opens_with_its_result_and_no_content_actions()
    {
        using var rig = new Rig();
        var s = Session(rig, "Closed");
        var asked = Ask(rig, s, "x1", "Closed question?", ["A", "B"], "A");
        using var context = Context(rig);
        var open = context.Render<Open>(p => p.Add(x => x.Id, asked.Id));
        Assert.That(open.FindAll(".pagebar .cfab"), Has.Count.EqualTo(1), "an open item has its content actions in the corner button");
        open.Find(".pagebar .cfab").Click();
        Assert.That(open.FindAll(".dial [data-act]").Select(b => b.GetAttribute("data-act")), Is.EqualTo(new[] { "accept", "other", "skip" }));

        new Messages(rig.Store).Answer(s, "x1", "B");
        var closed = context.Render<Open>(p => p.Add(x => x.Id, asked.Id));
        Assert.That(closed.FindAll(".pagebar .cfab"), Is.Empty, "a closed item has no content actions");
        Assert.That(closed.Find(".pagebar a.btn.pri").GetAttribute("href"), Is.EqualTo($"sessions/{s}"));
        Assert.That(closed.Find(".pill").TextContent, Is.EqualTo("answered"));
        Assert.That(closed.Markup, Does.Contain("Result"));
    }

    [Test]
    public void Sessions_fold_their_children_and_the_status_page_shows_them()
    {
        using var rig = new Rig();
        var root = Session(rig, "Fold root");
        Session(rig, "Fold child", root);
        using var context = Context(rig);
        var list = context.Render<Sessions>();
        var fold = list.Find("details.fold");
        Assert.That(fold.QuerySelector("summary")!.TextContent, Does.Contain("Fold root"));
        Assert.That(fold.TextContent, Does.Contain("Fold child"));

        var status = context.Render<SessionStatus>(p => p.Add(x => x.Id, root));
        Assert.That(status.Markup, Does.Contain("Children · 1"));
        Assert.That(status.Markup, Does.Contain("Fold child"));
        Assert.That(status.Find("a.btn.pri").GetAttribute("href"), Is.EqualTo($"stream?session={root}"));
    }

    [Test]
    public void Each_free_text_field_of_the_screens_has_a_mic_button()
    {
        using var rig = new Rig();
        var s = Session(rig, "Mics");
        Ask(rig, s, "m1", "Mic question?", ["A", "B"], null);
        using var context = Context(rig);
        var cards = context.Render<Cards>();
        cards.Find("[data-act=left]").Click();
        var status = context.Render<SessionStatus>(p => p.Add(x => x.Id, s));
        status.Find(".pagebar [data-act=message]").Click();
        foreach (var markup in new[] { cards.Markup, status.Markup })
        {
            var html = new HtmlParser().ParseDocument(markup);
            var areas = html.QuerySelectorAll("textarea").Select(t => t.Id).ToList();
            var mics = html.QuerySelectorAll("[data-speech-for]").Select(b => b.GetAttribute("data-speech-for")).ToList();
            Assert.That(areas, Is.Not.Empty);
            Assert.That(mics, Is.EquivalentTo(areas));
        }
    }
}

static class FieldsOf
{
    public static IReadOnlyDictionary<string, object?> Fields(this FileStore store, Event e) => store.Events.Fields(e);
}
