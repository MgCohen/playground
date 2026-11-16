using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;

namespace PatternEngine.Domains.Shapes;

/// <summary>Draws an expected pattern tree as SVG with fixed geometry. The drawing follows the shapes rules, so it is a known-good output.</summary>
public sealed class ShapesReference : IReferenceRenderer
{
    private const double W = 400;
    private const double H = 300;
    private const double Ground = 262;

    private static readonly Dictionary<string, string> Colors = new()
    {
        ["body"] = "#f3ead6", ["roof"] = "#7a7470", ["door"] = "brown", ["window"] = "#9fd3f0", ["chimney"] = "#a0522d",
        ["ground"] = "#7cb36b", ["trunk"] = "#8b5a2b", ["crown"] = "green",
    };

    private static readonly Dictionary<string, double> Factor = new() { ["house"] = 1.04, ["tower"] = 2.75, ["shed"] = 0.85, ["tree"] = 1.08 };

    private static readonly Dictionary<string, Func<TreeNode, double, double, double, List<string>>> Draw = new()
    {
        ["house"] = House,
        ["tower"] = Tower,
        ["shed"] = Shed,
        ["tree"] = Tree,
    };

    public string? Render(TreeNode tree, PatternCatalog catalog) => Svg(tree);

    public static string? Svg(TreeNode t)
    {
        string body;
        if (t.P == "street")
        {
            var kids = t.Parts ?? [];
            var slot = (W - 10) / Math.Max(kids.Count, 1);
            var objects = kids.Select((k, i) =>
            {
                var w = Math.Min(Math.Min(slot * 0.7, (Ground - 14) / Factor.GetValueOrDefault(k.P, 1)), 150);
                return Object(k, 5 + (i * slot) + ((slot - w) / 2), Ground, w);
            });
            body = $"<g data-p=\"street\" data-v=\"{t.V ?? "row"}\">{Leaf("ground", Rect(0, Ground, W, H - Ground, Colors["ground"]))}{string.Concat(objects)}</g>";
        }
        else if (Draw.ContainsKey(t.P))
        {
            var w = Math.Min(220, 255 / Factor.GetValueOrDefault(t.P, 1));
            body = Object(t, (W - w) / 2, 285, w);
        }
        else
        {
            return null;
        }

        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {W} {H}\">{body}</svg>";
    }

    public static string Object(TreeNode t, double x, double baseY, double w) =>
        Draw.TryGetValue(t.P, out var draw)
            ? $"<g data-p=\"{t.P}\" data-v=\"{t.V ?? "plain"}\">{string.Concat(draw(t, x, baseY, w))}</g>"
            : $"<g data-p=\"{t.P}\"></g>";

    private static string DoorFill(TreeNode t) => t.Params?.GetValueOrDefault("door_fill") ?? Colors["door"];

    private static List<string> House(TreeNode t, double x, double b, double w)
    {
        double bh = 0.62 * w, top = b - bh, rh = 0.42 * w;
        var parts = new List<string>();
        if (t.V == "chimney")
        {
            parts.Add(Leaf("chimney", Rect(x + (0.68 * w), top - (0.85 * rh), 0.12 * w, 0.5 * rh, Colors["chimney"])));
        }

        parts.Add(Leaf("body", Rect(x, top, w, bh, Colors["body"])));
        parts.Add(Leaf("roof", Tri([(x - (0.06 * w), top), (x + (0.5 * w), top - rh), (x + (1.06 * w), top)], Colors["roof"])));
        parts.Add(Leaf("door", Rect(x + (0.4 * w), b - (0.5 * bh), 0.2 * w, 0.5 * bh, DoorFill(t))));
        parts.Add(Leaf("window", Rect(x + (0.1 * w), top + (0.2 * bh), 0.18 * w, 0.18 * w, Colors["window"])));
        parts.Add(Leaf("window", Rect(x + (0.72 * w), top + (0.2 * bh), 0.18 * w, 0.18 * w, Colors["window"])));
        return parts;
    }

    private static List<string> Tower(TreeNode t, double x, double b, double w)
    {
        double bh = 2.2 * w, top = b - bh, rh = 0.55 * w;
        return
        [
            Leaf("body", Rect(x, top, w, bh, Colors["body"])),
            Leaf("roof", Tri([(x - (0.05 * w), top), (x + (0.5 * w), top - rh), (x + (1.05 * w), top)], Colors["roof"])),
            Leaf("door", Rect(x + (0.35 * w), b - (0.45 * w), 0.3 * w, 0.45 * w, DoorFill(t))),
            Leaf("window", Rect(x + (0.33 * w), top + (0.25 * w), 0.34 * w, 0.34 * w, Colors["window"])),
            Leaf("window", Rect(x + (0.33 * w), top + (0.95 * w), 0.34 * w, 0.34 * w, Colors["window"])),
        ];
    }

    private static List<string> Shed(TreeNode t, double x, double b, double w)
    {
        double bh = 0.55 * w, top = b - bh, rh = 0.3 * w;
        return
        [
            Leaf("body", Rect(x, top, w, bh, Colors["body"])),
            Leaf("roof", Tri([(x - (0.04 * w), top), (x + (0.5 * w), top - rh), (x + (1.04 * w), top)], Colors["roof"])),
            Leaf("door", Rect(x + (0.25 * w), b - (0.75 * bh), 0.5 * w, 0.75 * bh, DoorFill(t))),
        ];
    }

    private static List<string> Tree(TreeNode t, double x, double b, double w)
    {
        double tw = 0.16 * w, th = 0.55 * w, cx = x + (0.5 * w), cr = 0.38 * w, cy = b - th - (0.15 * w);
        return
        [
            Leaf("trunk", Rect(cx - (tw / 2), b - th, tw, th, Colors["trunk"])),
            Leaf("crown", $"<circle cx=\"{R(cx)}\" cy=\"{R(cy)}\" r=\"{R(cr)}\" fill=\"{Colors["crown"]}\"/>"),
        ];
    }

    private static string R(double n) => ShapesAdapter.JsNumber(ShapesAdapter.JsRound(n * 10) / 10.0);

    private static string Rect(double x, double y, double w, double h, string fill) =>
        $"<rect x=\"{R(x)}\" y=\"{R(y)}\" width=\"{R(w)}\" height=\"{R(h)}\" fill=\"{fill}\"/>";

    private static string Tri((double X, double Y)[] points, string fill) =>
        $"<polygon points=\"{string.Join(' ', points.Select(p => $"{R(p.X)},{R(p.Y)}"))}\" fill=\"{fill}\"/>";

    private static string Leaf(string p, string shape) => $"<g data-p=\"{p}\">{shape}</g>";
}
