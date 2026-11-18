using System.Text.Json;
using AngleSharp.Html.Parser;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Store;
using Orchestrator.Web;
using Orchestrator.Web.Components.Cards;
using Orchestrator.Web.Components.Pages;

namespace Orchestrator.Tests;

public sealed class NewSessionFormTests
{
    static BunitContext Context(Rig rig)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new Board(rig.Store, rig.Supervisor));
        context.Services.AddSingleton(new StreamHub(rig.Store));
        return context;
    }

    static IReadOnlyList<string> BriefProperties()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(Home.Templates(), "brief", "schema.json")));
        return schema.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
    }

    static JsonElement BriefEvent(Rig rig, string session) =>
        rig.Store.Events.Body(rig.Store.Query(new StreamQuery { Kinds = new HashSet<string> { EventKinds.Brief }, Session = session }).Single())!.Value;

    [Test]
    public void The_form_has_each_brief_field_except_allowDefaults_and_background()
    {
        // background (B2) is not in the form yet: the UI views are stream A4. A form session has Remote Control on, the default.
        using var context = new BunitContext();
        var form = context.Render<NewSessionForm>();
        var names = form.FindAll("form [name]").Select(e => e.GetAttribute("name")).ToList();
        Assert.That(names, Is.EquivalentTo(BriefProperties().Where(p => p is not ("allowDefaults" or "background"))));
        Assert.That(form.Markup, Does.Not.Contain("allowDefaults").IgnoreCase);
        Assert.That(form.Markup, Does.Not.Contain("default decisions").IgnoreCase);
    }

    [Test]
    public void Start_is_off_until_the_title_the_goal_and_a_folder_are_set()
    {
        using var context = new BunitContext();
        var form = context.Render<NewSessionForm>();
        bool Off() => form.Find("button[type=submit]").HasAttribute("disabled");
        Assert.That(Off(), Is.True);
        form.Find("#new-title").Input("Color child");
        form.Find("#new-goal").Input("Write RED in color.txt.");
        Assert.That(Off(), Is.True);
        form.Find("#new-cwd").Input("C:/work");
        Assert.That(Off(), Is.False);
        form.Find("#new-goal").Input("   ");
        Assert.That(Off(), Is.True);
    }

    [Test]
    public void The_form_starts_a_session_with_the_brief_fields()
    {
        using var rig = new Rig();
        using var context = Context(rig);
        var page = context.Render<Sessions>();
        page.Find("#new-title").Input(" Color child ");
        page.Find("#new-goal").Input("Write RED in color.txt.");
        page.Find("#new-scope").Input("Only color.txt.");
        page.Find("#new-done-when").Input("color.txt has RED.");
        page.Find("#new-cwd").Input("C:/work");
        page.Find("#new-agent").Input("worker");
        page.Find("#new-skills").Input("orch:orch-ask, orch:orch-report,");
        page.Find("#new-worktree").Change(true);
        page.Find("form").Submit();

        var id = rig.Store.AllSessions().Single();
        var meta = rig.Store.Session(id);
        Assert.That(meta.Title, Is.EqualTo("Color child"));
        Assert.That(meta.Work.Cwd, Is.EqualTo("C:/work"));
        Assert.That(meta.Type?.Agent, Is.EqualTo("worker"));
        Assert.That(meta.Type?.Skills, Is.EqualTo(new[] { "orch:orch-ask", "orch:orch-report" }));
        Assert.That(meta.Work.Worktree, Is.True);
        Assert.That(meta.Parent, Is.Null);
        Assert.That(meta.Settings.Flag(SettingTiers.Permissions, SettingTiers.AllowDefaults), Is.False, "a root copies the default");
        var brief = BriefEvent(rig, id);
        Assert.That(brief.GetProperty("goal").GetString(), Is.EqualTo("Write RED in color.txt."));
        Assert.That(brief.GetProperty("scope").GetString(), Is.EqualTo("Only color.txt."));
        Assert.That(brief.GetProperty("doneWhen").GetString(), Is.EqualTo("color.txt has RED."));
        Assert.That(brief.TryGetProperty("allowDefaults", out var allow) && allow.ValueKind != JsonValueKind.Null, Is.False);
        Assert.That(page.Find(".toast").TextContent, Is.EqualTo("Started Color child."));
        Assert.That(page.Find("#new-title").GetAttribute("value"), Is.Empty);
    }

    [Test]
    public void A_child_from_the_form_gets_the_parent_its_folder_and_a_copy_of_allowDefaults()
    {
        using var rig = new Rig();
        var parent = rig.Store.Create(new Seed { Title = "Root", Cwd = "C:/root", AllowDefaults = true }, "start");
        using var context = Context(rig);
        var page = context.Render<Sessions>();
        page.Find("#new-title").Input("Child");
        page.Find("#new-goal").Input("A small task.");
        page.Find("#new-parent").Change(parent);
        Assert.That(page.Find("#new-cwd").GetAttribute("placeholder"), Is.EqualTo("Default: C:/root"));
        page.Find("form").Submit();

        var meta = rig.Store.Session(rig.Store.AllSessions().Single(s => s != parent));
        Assert.That(meta.Parent, Is.EqualTo(parent));
        Assert.That(meta.Work.Cwd, Is.EqualTo("C:/root"));
        Assert.That(meta.Settings.Flag(SettingTiers.Permissions, SettingTiers.AllowDefaults), Is.True, "the brief has no allowDefaults; the registry copies the parent value at create");
    }

    [Test]
    public void A_refused_start_keeps_the_text()
    {
        using var context = new BunitContext();
        NewSession? sent = null;
        var form = context.Render<NewSessionForm>(p => p.Add(c => c.OnStart, input => { sent = input; return false; }));
        form.Find("#new-title").Input("Kept");
        form.Find("#new-goal").Input("Keep this text.");
        form.Find("#new-cwd").Input("C:/work");
        form.Find("form").Submit();

        Assert.That(sent, Is.Not.Null);
        Assert.That(sent!.AllowDefaults, Is.Null);
        Assert.That(form.Find("#new-title").GetAttribute("value"), Is.EqualTo("Kept"));
        Assert.That(form.Find("#new-goal").GetAttribute("value") ?? form.Find("#new-goal").TextContent, Is.EqualTo("Keep this text."));
    }

    [Test]
    public void Each_free_text_field_of_the_form_has_a_mic_button()
    {
        using var context = new BunitContext();
        var cards = new[] { context.Render<NewSessionForm>().Markup };
        foreach (var markup in cards)
        {
            var html = new HtmlParser().ParseDocument(markup);
            var areas = html.QuerySelectorAll("textarea").Select(t => t.Id).ToList();
            var mics = html.QuerySelectorAll("[data-speech-for]").Select(b => b.GetAttribute("data-speech-for")).ToList();
            Assert.That(areas, Is.Not.Empty);
            Assert.That(mics, Is.EquivalentTo(areas));
        }
    }
}
