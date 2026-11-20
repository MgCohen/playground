namespace Orchestrator.Web.Features.Motion.Domain;

public static class WindowSteps
{
    public static readonly WindowStep PanelShow = new("panel-show", "start", "shown");

    public static readonly WindowStep PanelActivate = new("panel-activate", "open", "activated");

    public static readonly WindowStep BadgeShow = new("badge-show", "badge-out", "shown");

    public static readonly WindowStep AllHide = new("all-hide", "hidden", "hidden");

    public static IReadOnlyList<WindowStep> All { get; } = [PanelShow, PanelActivate, BadgeShow, AllHide];

    public static WindowStep? For(string phase) => All.FirstOrDefault(s => s.Phase == phase);
}
