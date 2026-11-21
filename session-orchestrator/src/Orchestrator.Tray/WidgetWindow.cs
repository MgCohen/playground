using Orchestrator.Web;

namespace Orchestrator.Tray;

/// <summary>
/// The IWidgetWindow of the views: the two tray windows behind one contract. The panel window shows for the start, open and
/// folded phases; the badge window for badge-out and badge. Only one of them shows at a time. The tray app sets the windows
/// after it makes them. Before that, the calls do nothing.
/// </summary>
public sealed class WidgetWindow : IWidgetWindow
{
    TrayWindow? window;

    public TrayWindow? Window
    {
        get => window;
        set
        {
            window = value;
            if (window is null) return;
            window.PinnedChanged += () => PinnedChanged?.Invoke();
            window.Requested += what => Requested?.Invoke(what);
        }
    }

    BadgeWindow? badge;

    public BadgeWindow? Badge
    {
        get => badge;
        set
        {
            badge = value;
            if (badge is not null) badge.Hovered += on => BadgeHovered?.Invoke(on);
        }
    }

    public event Action<bool>? BadgeHovered;

    public void BadgeFolded() => Badge?.BeginInvoke(() => Badge.HoverEnded());

    public bool Pinned => Window?.Pinned ?? false;

    /// <summary>The label in the corner of the panel: the run code and the build, for example "I17 · 838b208" or "tray · 838b208".</summary>
    public string? Label { get; set; }

    /// <summary>False for a start with --hidden: only the tray icon shows.</summary>
    public bool OpenAtStart { get; set; } = true;

    public event Action? PinnedChanged;

    public event Action<string>? Requested;

    public void SetPinned(bool pinned)
    {
        if (Window is { } w) w.Pinned = pinned;
    }

    public void Minimize() => Requested?.Invoke(TrayRequest.Dock);

    public void Close() => Requested?.Invoke(TrayRequest.Hide);

    /// <summary>The flow enters a phase: the window of the phase shows, the other hides. The task ends when that is done.</summary>
    public Task Enter(string phase)
    {
        if (Window is not { } panel) return Task.CompletedTask;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        panel.BeginInvoke(() =>
        {
            Faults.Trace($"tray phase {phase} (from {panel.Phase})");
            panel.Phase = phase;
            switch (phase)
            {
                case TrayPhase.Start:
                    Badge?.Hide();
                    panel.ShowAtPlace();
                    LastEvent = panel.Visible ? "shown" : "not shown";
                    break;
                case TrayPhase.Open:
                    Faults.Trace("panel window activate");
                    panel.Activate();
                    LastEvent = Form.ActiveForm == panel ? "activated" : "not activated";
                    break;
                case TrayPhase.BadgeOut:
                    panel.Hide();
                    Badge?.ShowAtPlace();
                    LastEvent = Badge?.Visible == true && !panel.Visible ? "shown" : "not shown";
                    break;
                case TrayPhase.Hidden:
                    panel.Hide();
                    Badge?.Hide();
                    LastEvent = !panel.Visible && Badge?.Visible != true ? "hidden" : "not hidden";
                    break;
            }
            done.SetResult();
        });
        return done.Task;
    }

    public string Phase => Window?.Phase ?? TrayPhase.Hidden;

    /// <summary>
    /// The window event of the last Enter (S9). Show and Hide raise VisibleChanged at once, so the state after the call is the
    /// event. The activation is the foreground window after Activate.
    /// </summary>
    public string LastEvent { get; private set; } = "";

    public void Fault(Exception error, string where) => Faults.Add(error, where);


    /// <summary>R2: a report with badge boxes comes from the badge page; a report with none comes from the panel page.</summary>
    public void UseSizes(Orchestrator.Web.Features.Tray.Slices.Sizes.TraySizesResult sizes)
    {
        Faults.Trace($"sizes: {sizes}");
        if (sizes.Rest is null) Window?.BeginInvoke(() => Window.UseSizes(sizes));
        else Badge?.BeginInvoke(() => Badge.UseSizes(sizes));
    }

    public void Peek(bool on) => Badge?.BeginInvoke(() => Badge.Peek(on));
}
