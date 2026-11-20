namespace Orchestrator.Web.Features.Motion.Domain;

public static class WindowSteps
{
    public const string Shown = "shown";

    public const string Hidden = "hidden";

    public const string Activated = "activated";

    public static readonly WindowStep PanelShow = new("panel-show", TrayPhase.Start, Shown);

    public static readonly WindowStep PanelActivate = new("panel-activate", TrayPhase.Open, Activated);

    public static readonly WindowStep BadgeShow = new("badge-show", TrayPhase.BadgeOut, Shown);

    public static readonly WindowStep AllHide = new("all-hide", TrayPhase.Hidden, Hidden);

    public static IReadOnlyList<WindowStep> All { get; } = [PanelShow, PanelActivate, BadgeShow, AllHide];

    public static WindowStep? For(string phase) => All.FirstOrDefault(s => s.Phase == phase);
}
