using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Orchestrator.Core.Templates;

/// <summary>The view.json of a type: how the UI shows a message of this type as a full document and as a preview.</summary>
public sealed record ViewSpec(string Label, string Icon, string Tone, DocumentSpec Document, PreviewSpec Preview)
{
    /// <summary>Optional: a field whose value changes the tone and the icon, for example a blocked report.</summary>
    public ToneBySpec? ToneBy { get; init; }
}

public sealed record ToneBySpec(string Field, IReadOnlyDictionary<string, string> Values)
{
    public IReadOnlyDictionary<string, string>? Icons { get; init; }
}

/// <summary>The full document: a title, an optional subtitle, short facts in one table, then the blocks in order.</summary>
public sealed record DocumentSpec(IReadOnlyList<string> Title, IReadOnlyList<FieldSpec> Facts, IReadOnlyList<FieldSpec> Blocks)
{
    public IReadOnlyList<string>? Subtitle { get; init; }
}

/// <summary>The preview: the card in the pile and the row in the stream.</summary>
public sealed record PreviewSpec(IReadOnlyList<string> Title)
{
    /// <summary>The second line of the stream row: the first template that is not empty.</summary>
    public IReadOnlyList<string>? Line { get; init; }

    /// <summary>The well of the card: the first field that has a value.</summary>
    public IReadOnlyList<FieldSpec>? Well { get; init; }

    public FieldSpec? Chips { get; init; }
    public FieldSpec? List { get; init; }
    public FieldSpec? Pill { get; init; }

    /// <summary>The field with the percent done, for a progress bar.</summary>
    public string? Progress { get; init; }
}

/// <summary>One field in a view. <c>As</c> names the look; a form can use the same field, label and order.</summary>
public sealed record FieldSpec(string? Field, string? Label)
{
    /// <summary>text (default), code, well, list, steps, chips, links, pill or progress.</summary>
    public string As { get; init; } = Looks.Text;

    /// <summary>A field whose value is marked in the chips, for example the default option.</summary>
    public string? Mark { get; init; }

    /// <summary>The color token of each value, for a pill.</summary>
    public IReadOnlyDictionary<string, string>? Tones { get; init; }

    /// <summary>The color token of the block.</summary>
    public string? Tone { get; init; }

    /// <summary>A field whose value names a render file of the type: the block shows that render.</summary>
    public string? Render { get; init; }

    /// <summary>The maximum count of items in a preview list.</summary>
    public int? Max { get; init; }
}

public static class Looks
{
    public const string Text = "text";
    public const string Code = "code";
    public const string Well = "well";
    public const string List = "list";
    public const string Steps = "steps";
    public const string Chips = "chips";
    public const string Links = "links";
    public const string Pill = "pill";
    public const string Progress = "progress";
}

/// <summary>One short fact of a document, or the pill of a preview.</summary>
public sealed record Fact(string Field, string Label, string Text, string As, string? Tone);

/// <summary>One block of a document. Items has the values of a list field; Text has the value of a text field.</summary>
public sealed record Block(string Field, string Label, string As, string Text, IReadOnlyList<string> Items, string? Tone)
{
    public string? Marked { get; init; }
}

public sealed record DocumentModel(string Kind, string Label, string Icon, string Tone, string Title, string? Subtitle, IReadOnlyList<Fact> Facts, IReadOnlyList<Block> Blocks);

public sealed record Well(string Label, string Text);

public sealed record PreviewModel(string Kind, string Label, string Icon, string Tone, string Title, string? Line, Well? Well, IReadOnlyList<string> Chips, string? Marked, IReadOnlyList<string> List, Fact? Pill, double? Progress);

/// <summary>Turns the fields of a message into the document model and the preview model of its type.</summary>
public static partial class Views
{
    const int PreviewWellItems = 3;

    public static DocumentModel Document(TemplateSet set, string kind, IReadOnlyDictionary<string, object?> fields)
    {
        var view = Spec(set, kind);
        var (tone, icon) = Look(view, fields);
        var d = view.Document;
        return new DocumentModel(kind, view.Label, icon, tone,
            First(set, kind, d.Title, fields) ?? view.Label,
            d.Subtitle is null ? null : First(set, kind, d.Subtitle, fields),
            d.Facts.Select(f => ToFact(f, fields)).OfType<Fact>().ToList(),
            d.Blocks.Select(b => ToBlock(set, kind, b, fields)).OfType<Block>().ToList());
    }

    public static PreviewModel Preview(TemplateSet set, string kind, IReadOnlyDictionary<string, object?> fields)
    {
        var view = Spec(set, kind);
        var (tone, icon) = Look(view, fields);
        var p = view.Preview;
        var well = p.Well?.Select(w => Items(Value(fields, w.Field)) is { Count: > 0 } items ? new Well(w.Label ?? w.Field!, string.Join(" · ", items.Take(PreviewWellItems))) : null).FirstOrDefault(w => w is not null);
        var list = p.List is { } l ? Items(Value(fields, l.Field)).Take(l.Max ?? int.MaxValue).ToList() : [];
        return new PreviewModel(kind, view.Label, icon, tone,
            First(set, kind, p.Title, fields) ?? view.Label,
            p.Line is null ? null : First(set, kind, p.Line, fields),
            well,
            p.Chips is { } c ? Items(Value(fields, c.Field)) : [],
            p.Chips?.Mark is { } mark ? Text(Value(fields, mark)) is { Length: > 0 } m ? m : null : null,
            list,
            p.Pill is { } pill ? ToFact(pill, fields) : null,
            p.Progress is { } pr ? Percent(Value(fields, pr)) : null);
    }

    static ViewSpec Spec(TemplateSet set, string kind) => set[kind].View ?? throw new InvalidOperationException($"template {kind} has no view.json");

    /// <summary>The tone and the icon of a message of this kind: the view, changed by toneBy.</summary>
    public static (string Tone, string Icon) Look(TemplateSet set, string kind, IReadOnlyDictionary<string, object?> fields) => Look(Spec(set, kind), fields);

    static (string Tone, string Icon) Look(ViewSpec view, IReadOnlyDictionary<string, object?> fields)
    {
        if (view.ToneBy is not { } by) return (view.Tone, view.Icon);
        var value = Text(Value(fields, by.Field));
        return (by.Values.GetValueOrDefault(value) ?? view.Tone, by.Icons?.GetValueOrDefault(value) ?? view.Icon);
    }

    static Fact? ToFact(FieldSpec spec, IReadOnlyDictionary<string, object?> fields)
    {
        var value = Value(fields, spec.Field);
        if (!IsPresent(value)) return null;
        var text = spec.As == Looks.Progress && Percent(value) is { } p ? $"{p.ToString("0.#", CultureInfo.InvariantCulture)} %" : Text(value);
        return new Fact(spec.Field!, spec.Label ?? spec.Field!, text, spec.As, spec.Tones?.GetValueOrDefault(text));
    }

    static Block? ToBlock(TemplateSet set, string kind, FieldSpec spec, IReadOnlyDictionary<string, object?> fields)
    {
        if (spec.Render is { } render)
        {
            var text = RenderOf(set, kind, render, fields);
            return text is { Length: > 0 } ? new Block(render, spec.Label ?? render, spec.As, text, [], spec.Tone) : null;
        }
        var value = Value(fields, spec.Field);
        if (!IsPresent(value)) return null;
        var marked = spec.Mark is { } mark && Text(Value(fields, mark)) is { Length: > 0 } m ? m : null;
        return new Block(spec.Field!, spec.Label ?? spec.Field!, spec.As, value is string s ? s : Text(value), Items(value), spec.Tone) { Marked = marked };
    }

    /// <summary>The first template of the list that gives a text. {{render:field}} is the first line of the render that the field names.</summary>
    static string? First(TemplateSet set, string kind, IEnumerable<string> templates, IReadOnlyDictionary<string, object?> fields)
    {
        foreach (var template in templates)
        {
            if (Placeholders(template).Any(name => !IsPresent(Value(fields, name)))) continue;
            var text = RenderRef().Replace(template, m => FirstLine(RenderOf(set, kind, m.Groups[1].Value, fields) ?? ""));
            text = Render.Text(text, fields).Trim();
            if (text.Length > 0) return text;
        }
        return null;
    }

    /// <summary>The fields outside sections must all have a value, or the template does not apply.</summary>
    static IEnumerable<string> Placeholders(string template) => Render.Placeholders(SectionBlock().Replace(template, ""));

    static string? RenderOf(TemplateSet set, string kind, string field, IReadOnlyDictionary<string, object?> fields)
    {
        var name = Text(Value(fields, field));
        return name.Length > 0 && set[kind].HasRender(name) ? set.Render(kind, fields, name) : null;
    }

    static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();

    static object? Value(IReadOnlyDictionary<string, object?> fields, string? name) => name is not null && fields.TryGetValue(name, out var v) ? v : null;

    static bool IsPresent(object? value) => value switch
    {
        null => false,
        string s => s.Length > 0,
        ICollection c => c.Count > 0,
        _ => true,
    };

    static double? Percent(object? value) => value switch
    {
        double d => d is > 0 and <= 1 ? d * 100 : d,
        int i => i,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    static string Text(object? value) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "yes" : "no",
        double d => d.ToString(CultureInfo.InvariantCulture),
        IEnumerable items => string.Join(", ", items.Cast<object?>().Select(Text)),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    static IReadOnlyList<string> Items(object? value) => value switch
    {
        null => [],
        string s => s.Length > 0 ? [s] : [],
        IEnumerable items => items.Cast<object?>().Select(Text).Where(t => t.Length > 0).ToList(),
        _ => [Text(value)],
    };

    [GeneratedRegex(@"\{\{render:(\w+)\}\}")]
    private static partial Regex RenderRef();

    [GeneratedRegex(@"\{\{#(\w+)\}\}.*?\{\{/\1\}\}", RegexOptions.Singleline)]
    private static partial Regex SectionBlock();
}
