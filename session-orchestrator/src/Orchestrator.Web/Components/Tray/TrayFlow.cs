using System.Diagnostics;
using Orchestrator.Web.Features.Motion.Domain;
using Orchestrator.Web.Plugins.Js.Contracts;

namespace Orchestrator.Web.Components.Tray;

/// <summary>
/// The order of the two tray windows: the panel (TrayStage) and the badge (BadgeRoot). Each page plays its own motion; the flow
/// only orders the steps and asks the window for each phase (<see cref="IWidgetWindow.Enter"/>). One request runs at a time;
/// a request during a run waits, and only the last one runs. This queue orders window requests, not JS calls, so it stays here
/// (plain C#) and not in the JS pipeline. Only the tray app has a flow (a singleton): the web server has
/// none.
/// </summary>
public sealed class TrayFlow
{
    /// <summary>The motion of the panel page. Each task ends when its motion ends.</summary>
    public interface IPanel
    {
        /// <summary>The start phase: only the home ball place, measured for the clip circle.</summary>
        Task Prepare();

        /// <summary>The ball pops in and the panel grows; the rings draw at the end.</summary>
        Task Grow();

        /// <summary>The content fades, the panel folds into the ball, the ball pops out.</summary>
        Task Fold();
    }

    /// <summary>The motion of the badge page.</summary>
    public interface IBadge
    {
        /// <summary>The tab slides in from the screen edge.</summary>
        Task In();

        /// <summary>The tab slides out to the screen edge.</summary>
        Task Out();
    }

    readonly IWidgetWindow window;
    readonly TaskCompletionSource<IPanel> panel = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource<IBadge> badge = new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool busy;
    string? pending;
    TaskCompletionSource<bool>? notice;
    string? running;

    readonly IJsTrace? trace;
    readonly Stopwatch clock = Stopwatch.StartNew();
    long pageEnd;

    public TrayFlow(IWidgetWindow window, IJsTrace? trace = null)
    {
        this.window = window;
        this.trace = trace;
        window.Requested += what => _ = Request(what);
    }

    /// <summary>The phase of the tray (<see cref="TrayPhase"/>).</summary>
    public string Phase { get; private set; } = TrayPhase.Hidden;

    public void Attach(IPanel stage) => panel.TrySetResult(stage);

    public void Attach(IBadge stage) => badge.TrySetResult(stage);

    /// <summary>
    /// True when the tray is closed for the user: hidden or at the badge, or on the way there (a hide or a dock runs). An open
    /// that runs counts as open. The badge notice (TrayNotices) uses it.
    /// </summary>
    public bool Closed => running switch
    {
        TrayRequest.Open => false,
        TrayRequest.Hide or TrayRequest.Dock => true,
        _ => Phase is TrayPhase.Hidden or TrayPhase.BadgeOut or TrayPhase.Badge,
    };

    public async Task Request(string what)
    {
        if (busy)
        {
            pending = what;
            return;
        }
        running = what;
        await Run(what switch
        {
            TrayRequest.Open => Open,
            TrayRequest.Dock => Dock,
            TrayRequest.Hide => Hide,
            _ => () => Task.CompletedTask,
        });
    }

    /// <summary>
    /// A new card while the tray is hidden: the badge comes in. True when the badge shows after the call. H4: when the flow is
    /// busy, the notice waits for the end of the run; the badge then comes in only if the tray is hidden, so a notice never
    /// brings the badge over a panel that the user opened.
    /// </summary>
    public async Task<bool> BringBadge()
    {
        if (busy)
        {
            notice ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return await notice.Task;
        }
        if (Phase == TrayPhase.Hidden) await Run(ShowBadge);
        return Phase == TrayPhase.Badge;
    }

    // One run at a time. H3: an error of a step goes to the fault log of the tray, and the phase becomes the phase of the
    // windows, so the next request starts from what shows. After the run: the last waiting request, then a waiting notice.
    async Task Run(Func<Task> work)
    {
        busy = true;
        try
        {
            await work();
        }
        catch (Exception e)
        {
            window.Fault(e, $"tray flow (phase {Phase}, windows {window.Phase})");
            Phase = window.Phase;
        }
        finally
        {
            busy = false;
            running = null;
        }
        if (pending is { } next)
        {
            pending = null;
            await Request(next);
            return;
        }
        if (notice is { } waiting)
        {
            notice = null;
            if (Phase == TrayPhase.Hidden) await Run(ShowBadge);
            waiting.TrySetResult(Phase == TrayPhase.Badge);
        }
    }

    // S9: the one entry of a window step (WindowSteps). The window shows or hides, and the trace has the step, its window event,
    // its time and the window gap: the time from the end of the last page step.
    async Task Window(string phase)
    {
        var start = clock.ElapsedMilliseconds;
        await window.Enter(phase);
        if (WindowSteps.For(phase) is not { } step) return;
        trace?.Write($"window {step.Name}: {window.LastEvent} in {clock.ElapsedMilliseconds - start} ms, gap {start - pageEnd} ms");
    }

    // The one entry of a page step: the page plays its motion (through the motion pipeline) and the task ends with it.
    async Task Page(Func<Task> step)
    {
        await step();
        pageEnd = clock.ElapsedMilliseconds;
    }

    async Task Open()
    {
        if (Phase == TrayPhase.Open) return;
        if (Phase == TrayPhase.Badge)
        {
            await Page(async () => await (await badge.Task).Out());
            Phase = TrayPhase.BadgeOut;
        }
        var p = await panel.Task;
        // The panel window shows (and the badge window hides) while the page is in the start phase: nothing shows yet.
        await Window(TrayPhase.Start);
        Phase = TrayPhase.Start;
        await Page(() => p.Prepare());
        await Window(TrayPhase.Open);
        Phase = TrayPhase.Open;
        await Page(() => p.Grow());
    }

    async Task Dock()
    {
        if (Phase != TrayPhase.Open) return;
        await Page(async () => await (await panel.Task).Fold());
        Phase = TrayPhase.Folded;
        await ShowBadge();
    }

    async Task Hide()
    {
        if (Phase == TrayPhase.Open) await Page(async () => await (await panel.Task).Fold());
        else if (Phase == TrayPhase.Badge) await Page(async () => await (await badge.Task).Out());
        else if (Phase == TrayPhase.Hidden) return;
        await Window(TrayPhase.Hidden);
        Phase = TrayPhase.Hidden;
    }

    // The panel window hides and the badge window shows, with the tab out of view; then the tab slides in.
    async Task ShowBadge()
    {
        var b = await badge.Task;
        await Window(TrayPhase.BadgeOut);
        Phase = TrayPhase.BadgeOut;
        await Page(() => b.In());
        await Window(TrayPhase.Badge);
        Phase = TrayPhase.Badge;
    }
}
