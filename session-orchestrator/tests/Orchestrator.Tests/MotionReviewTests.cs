using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Orchestrator.Core.Contract;
using Orchestrator.Web;
using Orchestrator.Web.App.Host;
using Orchestrator.Web.Components.Motion;
using Orchestrator.Web.Components.Pages;
using Orchestrator.Web.Features.Motion.Slices.Wait;
using Orchestrator.Web.Plugins.Js.Contracts.Commands;
using Orchestrator.Web.Plugins.Js.Infrastructure;
using Umpire.Messages.Contracts;
using Umpire.Vsa.Contracts.Commands;

namespace Orchestrator.Tests;

/// <summary>The fixes of the code review of the motion pipeline (R1).</summary>
public sealed class MotionReviewTests
{
    sealed class ThrowingJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new JSException("the page is gone");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new JSException("the page is gone");
    }

    static ICommandSender Commands(IJSRuntime js)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => js);
        OrchHost.Add(services, new ConfigurationBuilder().Build());
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<ICommandSender>();
    }

    /// <summary>Review 3: a JS error of a flow step throws, so the tray flow writes a fault (H3).</summary>
    [Test]
    public void A_flow_step_throws_on_a_JS_error()
    {
        var commands = Commands(new ThrowingJs());
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => commands.Must(new MotionWait(".tray-panel", WaitMode.Step)));
        Assert.That(error!.Message, Does.Contain("js.failed").And.Contain("orchMotion.wait"));
    }

    sealed record Renamed(string Text) : JsCommand<Unit>("orchLegacy.run");

    /// <summary>Review 10: the constructor gives a JS name that is not from the type.</summary>
    [Test]
    public void A_command_can_name_its_JS_function()
    {
        Assert.That(JsNames.NameOf(new Renamed("x")), Is.EqualTo("orchLegacy.run"));
        Assert.That(JsNames.ArgumentsOf(new Renamed("x")), Is.EqualTo(new object?[] { "x" }));
    }

    static (BunitContext Context, Rig Rig) Deck(double undoMs)
    {
        var rig = new Rig();
        var session = rig.Store.Create(new Seed { Title = "Deck", Cwd = "C:/x" }, "start");
        rig.Store.SaveDecision(new Decision("d1", session) { Question = "First?", Options = ["A", "B"], DefaultOption = "A", Risk = "medium" });
        rig.Store.SaveDecision(new Decision("d2", session) { Question = "Second?", Options = ["A", "B"], DefaultOption = "A", Risk = "medium" });
        var context = UiContext.New();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<double>("orchMotion.ms", "--t-undo").SetResult(undoMs);
        context.Services.AddSingleton(new Board(rig.Store, rig.Supervisor));
        context.Services.AddSingleton(new StreamHub(rig.Store));
        return (context, rig);
    }

    /// <summary>Review 1: the throw class goes to the card of the act, and the wait targets that card by its id.</summary>
    [Test]
    public void The_throw_moves_the_card_of_the_act()
    {
        var (context, rig) = Deck(5000);
        using var owner = context;
        using var rigOwner = rig;
        var wait = context.JSInterop.Setup<WaitResult>("orchMotion.wait", _ => true);
        var page = context.Render<Cards>();
        var top = page.Find(".pcard[data-deck-card]").GetAttribute("data-card");

        page.Find("[data-act=right]").Click();

        var call = context.JSInterop.Invocations.Last(i => i.Identifier == "orchMotion.wait");
        Assert.That(call.Arguments[0], Is.EqualTo($"[data-card=\"{top}\"][data-motion~=\"deck-throw\"]"), "the wait targets the card of the act");
        Assert.That(page.Find($"[data-card=\"{top}\"]").ClassList, Does.Contain("out-r"));
        Assert.That(page.FindAll(".pcard.out-r"), Has.Count.EqualTo(1), "only the card of the act moves");
        wait.SetResult(new WaitResult(WaitResult.End, 300));
    }

    /// <summary>Review 2: with no undo time from the page, the act runs at once and the toast has no undo button.</summary>
    [Test]
    public void No_undo_time_gives_no_undo_button()
    {
        var (context, rig) = Deck(0);
        using var owner = context;
        using var rigOwner = rig;
        var page = context.Render<Cards>();

        page.Find("[data-act=right]").Click();

        page.WaitForAssertion(() => Assert.That(rig.Store.Events.FirstAnswer("d1") ?? rig.Store.Events.FirstAnswer("d2"), Is.Not.Null, "the act runs at once"));
        Assert.That(page.FindAll(".toast button"), Is.Empty, "no undo button that does nothing");
    }
}
