using Orchestrator.Web;

namespace Orchestrator.Tray;

/// <summary>The IWidgetWindow of the views. The tray app sets the form after it makes it. Before that, the calls do nothing.</summary>
public sealed class WidgetWindow : IWidgetWindow
{
    WidgetForm? form;

    public WidgetForm? Form
    {
        get => form;
        set
        {
            form = value;
            if (form is not null) form.PinnedChanged += () => PinnedChanged?.Invoke();
        }
    }

    public bool Pinned => Form?.Pinned ?? false;

    /// <summary>The label of a named instance (--instance), for example "v · 838b208"; null for the normal tray.</summary>
    public string? Label { get; set; }

    public event Action? PinnedChanged;

    public void SetPinned(bool pinned)
    {
        if (Form is { } f) f.Pinned = pinned;
    }

    public void Minimize() => Form?.Minimize();

    public void Close() => Form?.HideWidget();

    public void BeginDrag(double x, double y) => Form?.BeginDrag(x, y);
}
