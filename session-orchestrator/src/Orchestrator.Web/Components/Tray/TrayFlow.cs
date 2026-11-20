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
