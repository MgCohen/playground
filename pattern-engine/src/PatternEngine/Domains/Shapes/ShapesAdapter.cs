using System.Globalization;
using System.Text.RegularExpressions;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;

namespace PatternEngine.Domains.Shapes;

public sealed record Box(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;

    public double Height => Bottom - Top;

    public bool IsFinite => double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Right) && double.IsFinite(Bottom);

    public static Box? Union(IEnumerable<Box?> boxes)
    {
        var b = boxes.OfType<Box>().Where(x => x.IsFinite).ToList();
        return b.Count == 0 ? null : new Box(b.Min(x => x.Left), b.Min(x => x.Top), b.Max(x => x.Right), b.Max(x => x.Bottom));
    }
}

public sealed record SvgElement(string Type, IReadOnlyDictionary<string, string> Attrs, Box? Box);

public sealed class SvgNode(string pattern, string? variant) : OutputNode(pattern, variant)
{
    public List<SvgElement> Elements { get; } = [];

    public Box? Box { get; set; }

    public IEnumerable<SvgNode> Kids => Children.OfType<SvgNode>();
}

public sealed record SvgDocument(string? ViewBox, IReadOnlyList<string> Loose, IReadOnlyList<string> Unsupported, IReadOnlyList<string> Transforms);

/// <summary>Reads SVG drawings made of rect, polygon (triangle), circle and line. Nothing else in the engine knows SVG.</summary>
public sealed partial class ShapesAdapter : IOutputAdapter
{
    public const double CanvasWidth = 400;
    public const double CanvasHeight = 300;
    private const double Tolerance = 4;

    private static readonly HashSet<string> Drawn = ["rect", "polygon", "circle", "line", "ellipse", "path", "text", "image", "polyline"];
    private static readonly HashSet<string> Allowed = ["rect", "polygon", "circle", "line"];

    private static readonly (string Name, int[] Rgb)[] Named =
    [
        ("red", [220, 40, 40]), ("blue", [40, 80, 220]), ("green", [40, 160, 60]), ("yellow", [240, 210, 40]), ("orange", [240, 140, 30]),
        ("brown", [130, 80, 40]), ("black", [0, 0, 0]), ("white", [255, 255, 255]), ("gray", [128, 128, 128]), ("grey", [128, 128, 128]),
    ];

    private static readonly Dictionary<string, Func<Box, double>> Props = new(StringComparer.Ordinal)
    {
        ["top"] = b => b.Top,
        ["bottom"] = b => b.Bottom,
        ["left"] = b => b.Left,
        ["right"] = b => b.Right,
        ["width"] = b => b.Width,
        ["height"] = b => b.Height,
        ["cx"] = b => (b.Left + b.Right) / 2,
        ["cy"] = b => (b.Top + b.Bottom) / 2,
    };

    public ParsedOutput Parse(string text)
    {
        var errors = new List<string>();
        var root = new Frame(null);
        var stack = new Stack<Frame>();
        stack.Push(root);
        string? viewBox = null;
        var loose = new List<string>();
        var unsupported = new List<string>();
        var transforms = new List<string>();
        var body = Comments().Replace(text, string.Empty);
        body = Declarations().Replace(body, string.Empty);
        foreach (Match m in Tags().Matches(body))
        {
            var close = m.Groups[1].Value == "/";
            var tag = m.Groups[2].Value;
            var a = Attributes(m.Groups[3].Value);
            var selfClose = m.Groups[4].Value == "/";
            if (a.ContainsKey("transform"))
            {
                transforms.Add(tag);
            }

            if (tag == "svg" && !close)
            {
                viewBox = a.GetValueOrDefault("viewBox") ?? a.GetValueOrDefault("viewbox");
                continue;
            }

            if (tag == "g")
            {
                if (close)
                {
                    var n = stack.Pop();
                    if (n == root)
                    {
                        errors.Add("unbalanced </g>");
                        stack.Push(root);
                        continue;
                    }

                    if (n.Node is null)
                    {
                        var parent = stack.Peek();
                        parent.Children.AddRange(n.Children);
                        parent.Elements.AddRange(n.Elements);
                    }

                    continue;
                }

                var frame = a.TryGetValue("data-p", out var p) ? new Frame(new SvgNode(p, a.GetValueOrDefault("data-v"))) : new Frame(null);
                if (frame.Node is not null)
                {
                    stack.Peek().Children.Add(frame.Node);
                }

                if (!selfClose)
                {
                    stack.Push(frame);
                }

                continue;
            }

            if (Drawn.Contains(tag) && !close)
            {
                var el = new SvgElement(tag, a, ElementBox(tag, a));
                if (!Allowed.Contains(tag))
                {
                    unsupported.Add(tag);
                }

                var current = stack.Peek();
                if (current == root)
                {
                    loose.Add(tag);
                }

                current.Elements.Add(el);
            }
        }

        if (stack.Count > 1)
        {
            errors.Add("unclosed <g>");
        }

        foreach (var node in root.Children.OfType<SvgNode>())
        {
            ComputeBox(node);
        }

        return new ParsedOutput(root.Children, errors, new SvgDocument(viewBox, loose, unsupported, transforms));
    }

    public IReadOnlyList<string> Lint(ParsedOutput parsed)
    {
        var doc = (SvgDocument)parsed.Data!;
        var output = new List<string>();
        var vb = (doc.ViewBox ?? string.Empty).Trim().Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Select(JsNumber);
        if (string.Join(' ', vb) != $"0 0 {CanvasWidth} {CanvasHeight}")
        {
            output.Add($"viewBox must be \"0 0 {CanvasWidth} {CanvasHeight}\", got \"{doc.ViewBox ?? "-"}\"");
        }

        if (doc.Unsupported.Count > 0)
        {
            output.Add($"not allowed: {string.Join(", ", doc.Unsupported.Distinct())}");
        }

        if (doc.Transforms.Count > 0)
        {
            output.Add("transform is not allowed");
        }

        if (doc.Loose.Count > 0)
        {
            output.Add($"{doc.Loose.Count} shape(s) outside a pattern group");
        }

        void Walk(SvgNode n)
        {
            if (n.Box is { } b && (b.Left < -1 || b.Top < -1 || b.Right > CanvasWidth + 1 || b.Bottom > CanvasHeight + 1))
            {
                output.Add($"{n.Pattern} is outside the canvas");
            }

            foreach (var e in n.Elements.Where(e => string.IsNullOrEmpty(e.Attrs.GetValueOrDefault("fill")) && e.Type != "line"))
            {
                output.Add($"{n.Pattern}: {e.Type} has no fill");
            }

            foreach (var k in n.Kids)
            {
                Walk(k);
            }
        }

        foreach (var r in parsed.Roots.OfType<SvgNode>())
        {
            Walk(r);
        }

        return output;
    }

    public IReadOnlyList<string> CheckLeaf(OutputNode node, Pattern leaf)
    {
        var n = (SvgNode)node;
        if (n.Elements.Count != 1)
        {
            return [$"a leaf needs exactly one shape, got {n.Elements.Count}"];
        }

        var e = n.Elements[0];
        var messages = new List<string>();
        var want = leaf.Primitive switch { "rect" => "rect", "circle" => "circle", "line" => "line", "triangle" => "polygon", _ => null };
        if (e.Type != want)
        {
            messages.Add($"expected {leaf.Primitive}, got {e.Type}");
        }

        if (leaf.Primitive == "triangle")
        {
            var points = (e.Attrs.GetValueOrDefault("points") ?? string.Empty).Trim().Split([' ', ',', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (points.Length != 6)
            {
                messages.Add($"a triangle needs 3 points, got {JsNumber(points.Length / 2.0)}");
            }
        }

        if (e.Box is null)
        {
            messages.Add("shape has no size");
        }

        return messages;
    }

    public IReadOnlyList<AssertResult> Assert(OutputNode node, Pattern pattern, PatternCatalog catalog) =>
        pattern.Asserts.Select(a => AssertOne((SvgNode)node, a.Trim())).ToList();

    public string? ReadParam(OutputNode node, ParamDef param)
    {
        var part = node.Children.OfType<SvgNode>().FirstOrDefault(c => c.Pattern == param.Part);
        var el = part?.Elements.FirstOrDefault();
        if (el is null)
        {
            return null;
        }

        return el.Attrs.TryGetValue(param.Attr ?? "fill", out var v) ? ColorName(v) : null;
    }

    /// <summary>The nearest color name, so "#c62828" reads as "red".</summary>
    public static string ColorName(string color)
    {
        var rgb = ToRgb(color);
        if (rgb is null)
        {
            return color;
        }

        string best = string.Empty;
        var bestDistance = double.PositiveInfinity;
        foreach (var (name, value) in Named)
        {
            double d = 0;
            for (var i = 0; i < 3; i++)
            {
                d += Math.Pow(value[i] - rgb[i], 2);
            }

            if (d < bestDistance)
            {
                bestDistance = d;
                best = name;
            }
        }

        return best == "grey" ? "gray" : best;
    }

    /// <summary>A number as JavaScript prints it, so messages match the spike.</summary>
    public static string JsNumber(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    public static string JsNumber(string text) => JsNumber(Num(text));

    public static long JsRound(double value) => (long)Math.Floor(value + 0.5);

    internal static double Num(string? text)
    {
        if (text is null)
        {
            return double.NaN;
        }

        var m = LeadingNumber().Match(text.Trim());
        return m.Success && double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
    }

    private static AssertResult AssertOne(SvgNode node, string s)
    {
        try
        {
            Match m;
            if ((m = NoOverlap().Match(s)).Success)
            {
                var items = m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).SelectMany(n => Pick(node, n)).ToList();
                for (var i = 0; i < items.Count; i++)
                {
                    for (var j = i + 1; j < items.Count; j++)
                    {
                        if (Overlap(items[i].Box!, items[j].Box!))
                        {
                            return new AssertResult(s, false, $"{items[i].Pattern} and {items[j].Pattern} overlap");
                        }
                    }
                }

                return new AssertResult(s, true);
            }

            if ((m = CountAssert().Match(s)).Success)
            {
                var n = m.Groups[1].Value.Split(',').SelectMany(x => Pick(node, x.Trim())).Count();
                var k = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                var ok = m.Groups[2].Value switch { "==" => n == k, ">=" => n >= k, "<=" => n <= k, ">" => n > k, _ => n < k };
                return new AssertResult(s, ok, ok ? string.Empty : $"count is {n}");
            }

            if ((m = Inside().Match(s)).Success)
            {
                var items = Pick(node, m.Groups[2].Value).ToList();
                var b = Pick(node, m.Groups[3].Value).FirstOrDefault();
                if (items.Count == 0 || b is null)
                {
                    return new AssertResult(s, true, null, true);
                }

                var bad = items.Count(a => !IsInside(a.Box!, b.Box!));
                return new AssertResult(s, bad == 0, bad > 0 ? $"{bad} not inside" : string.Empty);
            }

            if ((m = Compare().Match(s)).Success)
            {
                var all = m.Groups[1].Success;
                var left = m.Groups[2].Value;
                var op = m.Groups[3].Value;
                var right = m.Groups[4].Value;
                if (all)
                {
                    var lm = PartProp().Match(left.Trim());
                    if (!lm.Success)
                    {
                        return new AssertResult(s, false, $"bad operand \"{left}\"");
                    }

                    var targets = Pick(node, lm.Groups[1].Value).ToList();
                    var r = Operand(node, right);
                    if (r.Missing || targets.Count == 0)
                    {
                        return new AssertResult(s, true, null, true);
                    }

                    var bad = targets.Count(t => !Cmp(Props[lm.Groups[2].Value](t.Box!), op, r.Value));
                    return new AssertResult(s, bad == 0, bad > 0 ? $"{bad} of {targets.Count} fail" : string.Empty);
                }

                var l = Operand(node, left);
                var rr = Operand(node, right);
                if (l.Missing || rr.Missing)
                {
                    return new AssertResult(s, true, null, true);
                }

                var good = Cmp(l.Value, op, rr.Value);
                return new AssertResult(s, good, good ? string.Empty : $"{JsRound(l.Value)} vs {JsRound(rr.Value)}");
            }

            return new AssertResult(s, false, "cannot read this assert");
        }
        catch (FormatException e)
        {
            return new AssertResult(s, false, e.Message);
        }
    }

    private static (double Value, bool Missing) Operand(SvgNode node, string s)
    {
        var m = OperandForm().Match(s.Trim());
        if (!m.Success)
        {
            if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            {
                return (n, false);
            }

            throw new FormatException($"bad operand \"{s}\"");
        }

        var target = Pick(node, m.Groups[1].Value).FirstOrDefault();
        if (target is null)
        {
            return (0, true);
        }

        if (!Props.TryGetValue(m.Groups[2].Value, out var prop))
        {
            throw new FormatException($"bad property \"{m.Groups[2].Value}\"");
        }

        var k = m.Groups[3].Success ? double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 1;
        return (prop(target.Box!) * k, false);
    }

    private static bool Cmp(double a, string op, double b) => op switch
    {
        "==" => Math.Abs(a - b) <= Tolerance,
        ">=" => a >= b - Tolerance,
        "<=" => a <= b + Tolerance,
        ">" => a > b,
        _ => a < b,
    };

    private static IEnumerable<SvgNode> Pick(SvgNode node, string name) => name == "self" ? [node] : node.Kids.Where(c => c.Pattern == name);

    private static bool IsInside(Box a, Box b) => a.Left >= b.Left - 2 && a.Right <= b.Right + 2 && a.Top >= b.Top - 2 && a.Bottom <= b.Bottom + 2;

    private static bool Overlap(Box a, Box b)
    {
        var w = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        var h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return w > 2 && h > 2;
    }

    private static Box? ComputeBox(SvgNode n)
    {
        foreach (var c in n.Kids)
        {
            ComputeBox(c);
        }

        n.Box = Box.Union(n.Elements.Select(e => e.Box).Concat(n.Kids.Select(c => c.Box)));
        return n.Box;
    }

    private static Box? ElementBox(string type, IReadOnlyDictionary<string, string> a)
    {
        double A(string key, string? fallback = null) => Num(a.GetValueOrDefault(key) ?? fallback);
        switch (type)
        {
            case "rect":
                return new Box(A("x", "0"), A("y", "0"), A("x", "0") + A("width"), A("y", "0") + A("height"));
            case "circle":
                return new Box(A("cx") - A("r"), A("cy") - A("r"), A("cx") + A("r"), A("cy") + A("r"));
            case "ellipse":
                return new Box(A("cx") - A("rx"), A("cy") - A("ry"), A("cx") + A("rx"), A("cy") + A("ry"));
            case "line":
                return new Box(Math.Min(A("x1"), A("x2")), Math.Min(A("y1"), A("y2")), Math.Max(A("x1"), A("x2")), Math.Max(A("y1"), A("y2")));
            case "polygon" or "polyline":
                var n = (a.GetValueOrDefault("points") ?? string.Empty).Trim().Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN).ToList();
                var xs = n.Where((_, i) => i % 2 == 0).ToList();
                var ys = n.Where((_, i) => i % 2 == 1).ToList();
                if (xs.Count == 0)
                {
                    return null;
                }

                return new Box(xs.Min(), ys.Count > 0 ? ys.Min() : double.NaN, xs.Max(), ys.Count > 0 ? ys.Max() : double.NaN);
            default:
                return null;
        }
    }

    private static int[]? ToRgb(string color)
    {
        var c = color.Trim().ToLowerInvariant();
        foreach (var (name, value) in Named)
        {
            if (name == c)
            {
                return value;
            }
        }

        Match m;
        if ((m = Hex3().Match(c)).Success)
        {
            return m.Groups[1].Value.Select(h => Convert.ToInt32($"{h}{h}", 16)).ToArray();
        }

        if ((m = Hex6().Match(c)).Success)
        {
            return [Convert.ToInt32(m.Groups[1].Value[..2], 16), Convert.ToInt32(m.Groups[1].Value[2..4], 16), Convert.ToInt32(m.Groups[1].Value[4..], 16)];
        }

        if ((m = RgbForm().Match(c)).Success)
        {
            return [int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)];
        }

        return null;
    }

    private static Dictionary<string, string> Attributes(string s)
    {
        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in AttributeForm().Matches(s))
        {
            output[m.Groups[1].Value] = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
        }

        return output;
    }

    [GeneratedRegex(@"<!--[\s\S]*?-->")]
    private static partial Regex Comments();

    [GeneratedRegex(@"<\?[\s\S]*?\?>")]
    private static partial Regex Declarations();

    [GeneratedRegex(@"<(\/?)([a-zA-Z][\w:-]*)([^>]*?)(\/?)>")]
    private static partial Regex Tags();

    [GeneratedRegex("([a-zA-Z_:][\\w:.-]*)\\s*=\\s*(\"([^\"]*)\"|'([^']*)')")]
    private static partial Regex AttributeForm();

    [GeneratedRegex(@"^no-overlap\s+(.+)$")]
    private static partial Regex NoOverlap();

    [GeneratedRegex(@"^count\(([^)]*)\)\s*(==|>=|<=|>|<)\s*(\d+)$")]
    private static partial Regex CountAssert();

    [GeneratedRegex(@"^(all\s+)?([\w-]+)\s+inside\s+([\w-]+)$")]
    private static partial Regex Inside();

    [GeneratedRegex(@"^(all\s+)?(.+?)\s*(==|>=|<=|>|<)\s*(.+)$")]
    private static partial Regex Compare();

    [GeneratedRegex(@"^([\w-]+)\.(\w+)$")]
    private static partial Regex PartProp();

    [GeneratedRegex(@"^([\w-]+)\.(\w+)(?:\s*\*\s*([\d.]+))?$")]
    private static partial Regex OperandForm();

    [GeneratedRegex(@"^[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?")]
    private static partial Regex LeadingNumber();

    [GeneratedRegex("^#([0-9a-f]{3})$")]
    private static partial Regex Hex3();

    [GeneratedRegex("^#([0-9a-f]{6})$")]
    private static partial Regex Hex6();

    [GeneratedRegex(@"^rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)$")]
    private static partial Regex RgbForm();

    private sealed class Frame(SvgNode? node)
    {
        public SvgNode? Node { get; } = node;

        public List<OutputNode> Children => Node?.Children ?? loose;

        public List<SvgElement> Elements => Node?.Elements ?? looseElements;

        private readonly List<OutputNode> loose = [];

        private readonly List<SvgElement> looseElements = [];
    }
}
