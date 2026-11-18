namespace Orchestrator.Web;

/// <summary>
/// The desktop widget window of the tray app (Orchestrator.Tray). The web server has none: then the header shows no pin.
/// </summary>
public interface IWidgetWindow
{
    bool Pinned { get; }

    /// <summary>The pin changed, from the header or from the tray menu.</summary>
    event Action? PinnedChanged;

    void SetPinned(bool pinned);

    /// <summary>
    /// Starts a drag of the window with the mouse button that is down. <paramref name="x"/> and <paramref name="y"/> are the
    /// CSS pixels of the mouse down in the page: that point stays under the pointer during the drag.
    /// </summary>
    void BeginDrag(double x, double y);
}
