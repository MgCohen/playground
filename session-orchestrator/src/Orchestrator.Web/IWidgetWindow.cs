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

    /// <summary>The label of a named tray instance (the name and the build), shown in the header; null for no label.</summary>
    string? Label => null;

    /// <summary>T4: the widget goes into its minimized form (the edge tab), which stays on the screen.</summary>
    void Minimize();

    /// <summary>T4: the widget hides. The tray icon opens it again.</summary>
    void Close();

    /// <summary>
    /// Starts a drag of the window with the mouse button that is down. <paramref name="x"/> and <paramref name="y"/> are the
    /// CSS pixels of the mouse down in the page: that point stays under the pointer during the drag.
    /// </summary>
    void BeginDrag(double x, double y);
}
