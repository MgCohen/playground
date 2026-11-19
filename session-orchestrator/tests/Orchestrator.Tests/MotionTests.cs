using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Web;
using Orchestrator.Web.Components.Motion;
using Orchestrator.Web.Components.Pages;

namespace Orchestrator.Tests;

/// <summary>The motion layer: the page move from the page depths, and the leave of a Presence.</summary>
public sealed class MotionTests
{
    [TestCase("", "cards", PageDepth.Tab)]
    [TestCase("cards", "settings", PageDepth.Tab)]
    [TestCase("stream", "events/e1", PageDepth.Push)]
    [TestCase("events/e1", "stream", PageDepth.Back)]
    [TestCase("sessions", "sessions/s1", PageDepth.Push)]
    [TestCase("sessions/s1", "stream?session=s1", PageDepth.Push)]
    [TestCase("stream?session=s1", "sessions/s1", PageDepth.Back)]
    [TestCase("stream", "stream?session=s1", PageDepth.Push)]
    [TestCase("timeline", "events/e1", PageDepth.Push)]
    public void The_move_comes_from_the_depth_of_the_two_pages(string from, string to, string move) =>
        Assert.That(PageDepth.Move(from, to), Is.EqualTo(move));

    [TestCase("cards", "cards")]
    [TestCase("events/e1", "events/e2")]
    [TestCase("sessions/s1", "sessions/s2")]
    [TestCase("cards", "no/such/page")]
    public void The_same_page_or_the_same_depth_below_the_tabs_is_no_move(string from, string to) =>
        Assert.That(PageDepth.Move(from, to), Is.Null);

    [Test]
    public void A_page_gives_its_depth_with_the_attribute()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PageDepth.Of(""), Is.EqualTo(0));
            Assert.That(PageDepth.Of("sessions/s1"), Is.EqualTo(1));
            Assert.That(PageDepth.Of("events/e1?x=1"), Is.EqualTo(2));
            Assert.That(PageDepth.Of("stream?session=s1"), Is.EqualTo(1.5));
        });
    }

    [Test]
    public void A_hidden_presence_leaves_and_then_tells_the_parent()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var gone = 0;
        var cut = context.Render<Presence>(p => p.AddChildContent("<p class=\"x\">block</p>").Add(x => x.OnGone, () => gone++));
        Assert.That(cut.Find(".presence.is-shown .x").TextContent, Is.EqualTo("block"));

        cut.Render(p => p.Add(x => x.Shown, false));

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".x"), Is.Empty));
        Assert.That(gone, Is.EqualTo(1));
        Assert.That(context.JSInterop.Invocations.Select(i => i.Identifier), Does.Contain("orchMotion.settle"), "the leave waits for the end of the CSS motion");
    }

    [Test]
    public void A_presence_that_starts_hidden_shows_nothing()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = context.Render<Presence>(p => p.AddChildContent("<p class=\"x\">block</p>").Add(x => x.Shown, false));
        Assert.That(cut.FindAll(".x"), Is.Empty);

        cut.Render(p => p.Add(x => x.Shown, true));

        Assert.That(cut.FindAll(".presence.is-shown .x"), Has.Count.EqualTo(1));
    }

    [Test]
    public void The_scrim_closes_the_filter_sheet_through_its_presence()
    {
        using var rig = new Rig();
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new Board(rig.Store, rig.Supervisor));
        context.Services.AddSingleton(new StreamHub(rig.Store));
        var page = context.Render<Cards>();

        page.Find(".fbtn").Click();
        Assert.That(page.FindAll(".presence .sheet"), Has.Count.EqualTo(1));
        page.Find(".presence .scrim").Click();

        page.WaitForAssertion(() => Assert.That(page.FindAll(".sheet"), Is.Empty));
    }

    [Test]
    public void A_render_of_the_parent_during_the_leave_does_not_stop_it()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        // The wait for the CSS motion stays open until the test ends it, as in a browser.
        var wait = context.JSInterop.SetupVoid("orchMotion.settle", _ => true);
        var gone = 0;
        var cut = context.Render<Presence>(p => p.AddChildContent("<p class=\"x\">block</p>").Add(x => x.OnGone, () => gone++));

        _ = cut.InvokeAsync(() => cut.Instance.Leave());
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".presence.is-leaving"), Has.Count.EqualTo(1)));
        // The parent renders again with the same parameters while the motion runs.
        cut.Render(p => p.AddChildContent("<p class=\"x\">block</p>").Add(x => x.OnGone, () => gone++));
        Assert.That(cut.FindAll(".presence.is-leaving"), Has.Count.EqualTo(1), "the leave goes on");
        wait.SetVoidResult();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".x"), Is.Empty));
        Assert.That(gone, Is.EqualTo(1));
    }
}
