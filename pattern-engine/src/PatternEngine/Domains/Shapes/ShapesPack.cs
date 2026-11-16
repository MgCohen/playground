using System.Net;
using System.Text.RegularExpressions;
using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Grading.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;

namespace PatternEngine.Domains.Shapes;

/// <summary>The shapes pack: simple SVG drawings made from rectangles, triangles, circles and lines.</summary>
public sealed class ShapesPack : IDomainPack
{
    public string Name => "shapes";

    public string Folder => "Shapes";

    public string OutputFile => "output.svg";

    public IOutputAdapter Adapter { get; } = new ShapesAdapter();

    public ILayout Layout { get; } = new ShapesLayout();

    public IReferenceRenderer? Reference { get; } = new ShapesReference();

    public IReadOnlyList<IMutationOperator> Mutations => ShapesMutations.All;

    public IReviewRenderer? Review { get; } = new ShapesReview();

    public IReadOnlyList<IGrader> Graders => [];
}

/// <summary>Code does the layout, so the agent draws one object in one box.</summary>
public sealed class ShapesLayout : ILayout
{
    private const double W = 400;
    private const double H = 300;
    private const double Ground = 262;

    public bool IsAssembled(string pattern) => pattern == "street";

    public IReadOnlyList<Slot> Slots(string? pattern, int children)
    {
        if (pattern == "street")
        {
            var n = Math.Max(children, 1);
            var slot = (W - 10) / n;
            return Enumerable.Range(0, children).Select(i => Box(Math.Round(5 + (i * slot) + (slot * 0.08), MidpointRounding.AwayFromZero), Math.Round(slot * 0.84, MidpointRounding.AwayFromZero), Ground, Ground - 12)).ToList();
        }

        return [Box(70, 260, 285, 275)];
    }

    public string SlotText(Slot b) =>
        $"Draw inside x from {b["x"]} to {b["x"] + b["w"]}. The bottom of the object is on y = {b["base"]}. The top is not above y = {b["base"] - b["maxH"]}. You can use less width than the box.";

    public string Assemble(string pattern, string? variant, IReadOnlyList<string> fragments) => pattern == "street"
        ? $"<g data-p=\"street\" data-v=\"{variant ?? "row"}\"><g data-p=\"ground\"><rect x=\"0\" y=\"{Ground}\" width=\"{W}\" height=\"{H - Ground}\" fill=\"green\"/></g>{string.Concat(fragments)}</g>"
        : throw new InvalidOperationException($"no assembly for {pattern}");

    public string Wrap(string fragment) => $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {W} {H}\">{fragment}</svg>";

    public IReadOnlyList<string> CheckSlot(OutputNode? root, Slot b)
    {
        var x = (root as SvgNode)?.Box;
        if (x is null)
        {
            return ["the fragment has no shapes"];
        }

        var output = new List<string>();
        if (x.Left < b["x"] - 3 || x.Right > b["x"] + b["w"] + 3)
        {
            output.Add($"the object goes outside x {b["x"]}..{b["x"] + b["w"]} (it is {ShapesAdapter.JsRound(x.Left)}..{ShapesAdapter.JsRound(x.Right)})");
        }

        if (Math.Abs(x.Bottom - b["base"]) > 4)
        {
            output.Add($"the bottom must be on y = {b["base"]} (it is {ShapesAdapter.JsRound(x.Bottom)})");
        }

        if (x.Top < b["base"] - b["maxH"] - 3)
        {
            output.Add($"the top is above y = {b["base"] - b["maxH"]}");
        }

        return output;
    }

    private static Slot Box(double x, double w, double baseY, double maxH) =>
        new(new Dictionary<string, double> { ["x"] = x, ["w"] = w, ["base"] = baseY, ["maxH"] = maxH });
}

public sealed partial class ShapesReview : IReviewRenderer
{
    public string RenderOutput(string output) => $"<div class=\"svg-frame\">{Clean(output)}</div>";

    public string? RenderExpected(Case c, PatternCatalog catalog)
    {
        if (c.Expect.Outcome == Outcomes.NoFit)
        {
            var near = c.Expect.Nearest ?? [];
            var svg = near.Count > 0 ? ShapesReference.Svg(new TreeNode { P = near[0], V = "plain" }) : null;
            var pic = svg is null ? string.Empty : $"<div class=\"svg-frame faded\">{svg}</div>";
            return $"{pic}<p class=\"caption\">No pattern fits. The engine must start from the nearest pattern ({WebUtility.HtmlEncode(string.Join(" or ", near))}) and change it.</p>";
        }

        var drawing = c.Expect.Tree is null ? null : ShapesReference.Svg(c.Expect.Tree);
        return drawing is null ? "<p class=\"none\">No reference drawing for this pattern.</p>" : $"<div class=\"svg-frame\">{drawing}</div>";
    }

    /// <summary>Removes anything that can run code. The SVG comes from an agent.</summary>
    private static string Clean(string svg)
    {
        svg = XmlDeclaration().Replace(svg, string.Empty);
        svg = Script().Replace(svg, string.Empty);
        svg = Foreign().Replace(svg, string.Empty);
        svg = Handler().Replace(svg, string.Empty);
        return ScriptLink().Replace(svg, string.Empty);
    }

    [GeneratedRegex(@"<\?xml[\s\S]*?\?>")]
    private static partial Regex XmlDeclaration();

    [GeneratedRegex(@"<script[\s\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex Script();

    [GeneratedRegex(@"<foreignObject[\s\S]*?</foreignObject>", RegexOptions.IgnoreCase)]
    private static partial Regex Foreign();

    [GeneratedRegex("\\son\\w+\\s*=\\s*(\"[^\"]*\"|'[^']*')", RegexOptions.IgnoreCase)]
    private static partial Regex Handler();

    [GeneratedRegex("(href|xlink:href)\\s*=\\s*(\"|')\\s*javascript:[^\"']*\\2", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptLink();
}
