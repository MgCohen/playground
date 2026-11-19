namespace Orchestrator.Web.Components.Tray;

/// <summary>
/// The order of the two tray windows: the panel (TrayStage) and the badge (BadgeRoot). Each page plays its own motion; the flow
/// only orders the steps and asks the window for each phase (<see cref="IWidgetWindow.Enter"/>). One request runs at a time;
/// a request during a run waits, and only the last one runs. Only the tray app has a flow (a singleton): the web server has
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

    public TrayFlow(IWidgetWindow window)
    {
        this.window = window;
        window.Requested += what => _ = Request(what);
    }

    /// <summary>The phase of the tray (<see cref="TrayPhase"/>).</summary>
    public string Phase { get; private set; } = TrayPhase.Hidden;

    public void Attach(IPanel stage) => panel.TrySetResult(stage);

    public void Attach(IBadge stage) => badge.TrySetResult(stage);

    public async Task Request(string what)
    {
        if (busy)
        {
            pending = what;
            return;
        }
        busy = true;
        try
        {
            if (what == TrayRequest.Open) await Open();
            else if (what == TrayRequest.Dock) await Dock();
            else if (what == TrayRequest.Hide) await Hide();
        }
        catch (Exception e) when (e is Microsoft.JSInterop.JSException or Microsoft.JSInterop.JSDisconnectedException or TaskCanceledException or InvalidOperationException)
        {
        }
        finally
        {
            busy = false;
        }
        if (pending is { } next)
        {
            pending = null;
            await Request(next);
        }
    }

    /// <summary>A new card while the tray is hidden: the badge comes in. True when the badge shows after the call.</summary>
    public async Task<bool> BringBadge()
    {
        if (Phase == TrayPhase.Hidden && !busy)
        {
            busy = true;
            try
            {
                await ShowBadge();
            }
            finally
            {
                busy = false;
            }
        }
        return Phase == TrayPhase.Badge;
    }

    async Task Open()
    {
        if (Phase == TrayPhase.Open) return;
        if (Phase == TrayPhase.Badge)
        {
            await (await badge.Task).Out();
            Phase = TrayPhase.BadgeOut;
        }
        var p = await panel.Task;
        // The panel window shows (and the badge window hides) while the page is in the start phase: nothing shows yet.
        await window.Enter(TrayPhase.Start);
        Phase = TrayPhase.Start;
        await p.Prepare();
        await window.Enter(TrayPhase.Open);
        Phase = TrayPhase.Open;
        await p.Grow();
    }

    async Task Dock()
    {
        if (Phase != TrayPhase.Open) return;
        await (await panel.Task).Fold();
        Phase = TrayPhase.Folded;
        await ShowBadge();
    }

    async Task Hide()
    {
        if (Phase == TrayPhase.Open) await (await panel.Task).Fold();
        else if (Phase == TrayPhase.Badge) await (await badge.Task).Out();
        else if (Phase == TrayPhase.Hidden) return;
        await window.Enter(TrayPhase.Hidden);
        Phase = TrayPhase.Hidden;
    }

    // The panel window hides and the badge window shows, with the tab out of view; then the tab slides in.
    async Task ShowBadge()
    {
        var b = await badge.Task;
        await window.Enter(TrayPhase.BadgeOut);
        Phase = TrayPhase.BadgeOut;
        await b.In();
        await window.Enter(TrayPhase.Badge);
        Phase = TrayPhase.Badge;
    }
}
