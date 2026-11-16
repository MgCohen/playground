using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;

namespace PatternEngine.Domains.Shapes;

public sealed record MutationOperator(string Id, IReadOnlyList<string> Expect, string Describe, Func<MutationInput, string?> Run) : IMutationOperator
{
    public string? Apply(MutationInput input) => Run(input);
}

/// <summary>Operators that break a known-good drawing in one way. The graders must fail each result, in the grader that Expect names.</summary>
public static partial class ShapesMutations
{
    public static IReadOnlyList<IMutationOperator> All { get; } =
    [
        new MutationOperator("remove-roof", ["structure"], "Remove the roof of the first building", i => RemoveLeaf(i.Output, "roof")),
        new MutationOperator("remove-ground", ["structure"], "Remove the ground of a street", i => RemoveLeaf(i.Output, "ground")),
        new MutationOperator("remove-chimney", ["structure"], "Remove the chimney of a chimney house", i => RemoveLeaf(i.Output, "chimney")),
        new MutationOperator("door-up", ["structure"], "Move the first door up, off the bottom of the body", i =>
        {
            var m = LeafRe("door").Match(i.Output);
            return m.Success ? ReplaceFirst(i.Output, m.Value, SetAttr(m.Value, "y", Num(m.Value, "y") - 25)) : null;
        }),
        new MutationOperator("window-over-door", ["structure"], "Put the first window on top of the door", i =>
        {
            var obj = Objects(i.Output).FirstOrDefault(o => LeafRe("window").IsMatch(o.Text) && LeafRe("door").IsMatch(o.Text));
            if (obj.Text is null)
            {
                return null;
            }

            var d = LeafRe("door").Match(obj.Text).Value;
            var w = LeafRe("window").Match(obj.Text).Value;
            var moved = SetAttr(SetAttr(w, "x", Num(d, "x")), "y", Num(d, "y"));
            return ReplaceFirst(i.Output, obj.Text, ReplaceFirst(obj.Text, w, moved));
        }),
        new MutationOperator("swap-children", ["built"], "Swap the first two objects of a street (wrong order)", i =>
        {
            var parts = i.Tree.Parts ?? [];
            if (i.Tree.P != "street" || parts.Count < 2 || JsonSerializer.Serialize(parts[0]) == JsonSerializer.Serialize(parts[1]))
            {
                return null;
            }

            var objects = Objects(i.Output);
            var (a, b) = (objects[0], objects[1]);
            var s = i.Output;
            return s[..a.Index] + b.Text + s[(a.Index + a.Text.Length)..b.Index] + a.Text + s[(b.Index + b.Text.Length)..];
        }),
        new MutationOperator("change-param", ["built"], "Paint the doors that have the asked color in another color", i =>
        {
            var color = AllParams(i.Tree).Select(p => p.GetValueOrDefault("door_fill")).FirstOrDefault(v => v is not null);
            if (color is null)
            {
                return null;
            }

            var re = new Regex($"(<g data-p=\"door\"><rect [^>]*fill=\"){Regex.Escape(color)}(\")");
            return re.IsMatch(i.Output) ? re.Replace(i.Output, "${1}black$2") : null;
        }),
        new MutationOperator("wrong-variant", ["structure", "built"], "Mark a chimney house as plain", i =>
            i.Output.Contains("data-v=\"chimney\"", StringComparison.Ordinal) ? ReplaceFirst(i.Output, "data-v=\"chimney\"", "data-v=\"plain\"") : null),
        new MutationOperator("wrong-type", ["built"], "Mark the first house as a tower", i =>
            i.Output.Contains("data-p=\"house\"", StringComparison.Ordinal) ? ReplaceFirst(i.Output, "data-p=\"house\"", "data-p=\"tower\"") : null),
        new MutationOperator("overlap", ["structure"], "Move the second object of a street onto the first", i =>
        {
            if (i.Tree.P != "street" || (i.Tree.Parts?.Count ?? 0) < 2)
            {
                return null;
            }

            var objects = Objects(i.Output);
            Box BoxOf(string t) => ((SvgNode)i.Adapter.Parse($"<svg viewBox=\"0 0 400 300\">{t}</svg>").Roots[0]).Box!;
            var (a, b) = (objects[0], objects[1]);
            return ReplaceFirst(i.Output, b.Text, ShiftX(b.Text, BoxOf(a.Text).Left - BoxOf(b.Text).Left));
        }),
        new MutationOperator("outside-canvas", ["rules"], "Move the first object out of the canvas", i =>
        {
            var objects = Objects(i.Output);
            return objects.Count > 0 ? ReplaceFirst(i.Output, objects[0].Text, ShiftX(objects[0].Text, 500)) : null;
        }),
        new MutationOperator("add-path", ["rules"], "Add a path element, which the rules forbid", i =>
        {
            var objects = Objects(i.Output);
            return objects.Count > 0 ? ReplaceFirst(i.Output, objects[0].Text, FirstTag().Replace(objects[0].Text, "$1<path d=\"M0 0L5 5\" fill=\"black\"/>", 1)) : null;
        }),
        new MutationOperator("no-fill", ["rules"], "Remove the fill of the first body", i =>
        {
            var m = LeafRe("body").Match(i.Output);
            return m.Success ? ReplaceFirst(i.Output, m.Value, FillAttr().Replace(m.Value, string.Empty, 1)) : null;
        }),
    ];

    private static Regex LeafRe(string name) => new($"<g data-p=\"{name}\"><[^>]*\\/><\\/g>");

    private static string? RemoveLeaf(string svg, string name)
    {
        var m = LeafRe(name).Match(svg);
        return m.Success ? svg.Remove(m.Index, m.Length) : null;
    }

    private static string ReplaceFirst(string s, string find, string with)
    {
        var at = s.IndexOf(find, StringComparison.Ordinal);
        return at < 0 ? s : s[..at] + with + s[(at + find.Length)..];
    }

    private static double Num(string s, string attr)
    {
        var m = new Regex($"\\s{attr}=\"([-\\d.]+)\"").Match(s);
        return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : double.NaN;
    }

    private static string SetAttr(string s, string attr, double v)
    {
        var value = ShapesAdapter.JsNumber(ShapesAdapter.JsRound(v * 10) / 10.0);
        return new Regex($"(\\s{attr}=\")[-\\d.]+(\")").Replace(s, $"${{1}}{value}$2", 1);
    }

    private static string ShiftX(string fragment, double dx)
    {
        var moved = Coordinates().Replace(fragment, m => m.Groups[1].Value + ShapesAdapter.JsNumber(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) + dx) + m.Groups[3].Value);
        return Points().Replace(moved, m =>
        {
            var pts = m.Groups[1].Value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(p =>
            {
                var xy = p.Split(',');
                return $"{ShapesAdapter.JsNumber(double.Parse(xy[0], CultureInfo.InvariantCulture) + dx)},{xy[1]}";
            });
            return $"points=\"{string.Join(' ', pts)}\"";
        });
    }

    private static List<(string Text, int Index)> Objects(string svg) => ObjectForm().Matches(svg).Select(m => (m.Value, m.Index)).ToList();

    private static IEnumerable<Dictionary<string, string>> AllParams(TreeNode t)
    {
        if (t.Params is not null)
        {
            yield return t.Params;
        }

        foreach (var p in (t.Parts ?? []).SelectMany(AllParams))
        {
            yield return p;
        }
    }

    [GeneratedRegex("<g data-p=\"(house|tower|shed|tree)\" data-v=\"[^\"]*\">(?:<g data-p=\"[a-z]+\"><[^>]*\\/><\\/g>)+<\\/g>")]
    private static partial Regex ObjectForm();

    [GeneratedRegex("(\\s(?:x|cx)=\")([-\\d.]+)(\")")]
    private static partial Regex Coordinates();

    [GeneratedRegex("points=\"([^\"]*)\"")]
    private static partial Regex Points();

    [GeneratedRegex("^(<g [^>]*>)")]
    private static partial Regex FirstTag();

    [GeneratedRegex("\\sfill=\"[^\"]*\"")]
    private static partial Regex FillAttr();
}
