using Orchestrator.Web;

namespace Orchestrator.Tray;

/// <summary>The IWidgetWindow of the views. The tray app sets the window after it makes it. Before that, the calls do nothing.</summary>
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

    public Task Enter(string phase) => Window?.EnterPhase(phase) ?? Task.CompletedTask;

    public void Peek(bool on) => Window?.Peek(on);
}
