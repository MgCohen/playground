namespace Orchestrator.Web;

/// <summary>
/// E-1: one act of a page bar or of the corner button. The role sets the look and the place: main (the accent button, first),
/// other, danger (red; a square icon button in the page bar, last in the corner list). An act with an address is a link.
/// </summary>
public sealed record PageAct(string Id, string Label, string Role = PageAct.Other, string? Icon = null, string? Href = null)
{
    public const string Main = "main";
    public const string Other = "other";
    public const string Danger = "danger";

    /// <summary>The acts in their order: main first, danger last.</summary>
    public static IReadOnlyList<PageAct> Ordered(IEnumerable<PageAct> acts) =>
        acts.OrderBy(a => a.Role switch { Main => 0, Danger => 2, _ => 1 }).ToList();
}
