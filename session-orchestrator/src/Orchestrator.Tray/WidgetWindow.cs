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

    public BadgeWindow? Badge { get; set; }

    public bool Pinned => Window?.Pinned ?? false;

    /// <summary>The label of a named instance (--instance), for example "v · 838b208"; null for the normal tray.</summary>
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
                    break;
                case TrayPhase.Open:
                    Faults.Trace("panel window activate");
                    panel.Activate();
                    break;
                case TrayPhase.BadgeOut:
                    panel.Hide();
                    Badge?.ShowAtPlace();
                    break;
                case TrayPhase.Hidden:
                    panel.Hide();
                    Badge?.Hide();
                    break;
            }
            done.SetResult();
        });
        return done.Task;
    }

    public string Phase => Window?.Phase ?? TrayPhase.Hidden;

    public void Fault(Exception error, string where) => Faults.Add(error, where);

    public void Peek(bool on) => Badge?.BeginInvoke(() => Badge.Peek(on));
}
