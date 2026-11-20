namespace Orchestrator.Web;

/// <summary>
/// The desktop windows of the tray app (Orchestrator.Tray): the panel window and the badge window. The web server has none: then
/// the header shows no window buttons and no tray stage renders. The tray flow (Components/Tray/TrayFlow) orders the phases; the
/// pages play all motion; the windows only show or hide for each phase, set their click areas and report the requests of the user.
/// </summary>
public interface IWidgetWindow
{
    bool Pinned { get; }

    /// <summary>The pin changed, from the header or from the tray menu.</summary>
    event Action? PinnedChanged;

    void SetPinned(bool pinned);

    /// <summary>The label of a named tray instance (the name and the build), shown in the header; null for no label.</summary>
    string? Label => null;

    /// <summary>True when the tray opens the panel at the start; false for a start in the tray icon only.</summary>
    bool OpenAtStart => true;

    /// <summary>A request of the user for the stage: <see cref="TrayRequest.Open"/>, <see cref="TrayRequest.Dock"/> or <see cref="TrayRequest.Hide"/>.</summary>
    event Action<string>? Requested;

    /// <summary>The minimize button of the header: the panel docks to the badge.</summary>
    void Minimize();

    /// <summary>The close button of the header: the tray hides. The tray icon opens it again.</summary>
    void Close();

    /// <summary>The flow enters a phase (<see cref="TrayPhase"/>). The task ends when the window of the phase shows and the other hides.</summary>
    Task Enter(string phase);

    /// <summary>The phase of the windows: the last phase that <see cref="Enter"/> made (H3: the flow reads it after a fault).</summary>
    string Phase => TrayPhase.Hidden;

    /// <summary>An error of the tray flow: the tray writes it to its fault log (H3: nothing is swallowed).</summary>
    void Fault(Exception error, string where)
    {
    }

    /// <summary>The badge shows its peek bubble (true) or hides it (false): the click area takes the bubble in or out.</summary>
    void Peek(bool on);
}

/// <summary>The requests of the user for the tray stage.</summary>
public static class TrayRequest
{
    public const string Open = "open", Dock = "dock", Hide = "hide";
}

/// <summary>
/// The phases of the tray. Start, open and folded show the panel window; badge-out and badge show the badge window; hidden shows
/// no window. Each phase is also a CSS class with the prefix "is-" (tray.css).
/// </summary>
public static class TrayPhase
{
    public const string Start = "start", Open = "open", Folded = "folded", BadgeOut = "badge-out", Badge = "badge", Hidden = "hidden";

    public static bool AtBadge(string phase) => phase is BadgeOut or Badge;
}
