namespace Orchestrator.Web.Features.Tray.Slices.Sizes;

using System.Globalization;

public sealed record TraySizesResult(
    double PanelWidth,
    double PanelHeight,
    double Margin,
    double Width,
    double[]? Rest,
    double[]? Hover,
    double[]? Bubble)
{
    public override string ToString() =>
        $"panel {PanelWidth} x {PanelHeight}, margin {Margin}; page width {Width}; "
        + $"rest {Text(Rest)}, hover {Text(Hover)}, bubble {Text(Bubble)}";

    static string Text(double[]? box) =>
        box is null
            ? "none"
            : $"[{string.Join(' ', box.Select(v => v.ToString("0.#", CultureInfo.InvariantCulture)))}]";
}
