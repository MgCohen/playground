using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Orchestrator.Core.Templates;

public static partial class Render
{
    public static string Text(string template, IReadOnlyDictionary<string, object?> data)
    {
        var withSections = Section().Replace(template, m => IsPresent(Value(data, m.Groups[1].Value)) ? Text(m.Groups[2].Value, data) : "");
        return Field().Replace(withSections, m => Format(Value(data, m.Groups[1].Value))).TrimEnd('\r', '\n');
    }

    public static IReadOnlyList<string> Placeholders(string template) =>
        Field().Matches(Section().Replace(template, m => m.Groups[2].Value)).Select(m => m.Groups[1].Value).Distinct().ToList();

    public static Dictionary<string, object?> Fields(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object ? element.EnumerateObject().ToDictionary(p => p.Name, p => FromJson(p.Value)) : [];

    static object? FromJson(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => value.EnumerateArray().Select(FromJson).ToList(),
        JsonValueKind.Object => Fields(value),
        _ => null,
    };

    static object? Value(IReadOnlyDictionary<string, object?> data, string name) => data.TryGetValue(name, out var value) ? value : null;

    static bool IsPresent(object? value) => value switch
    {
        null => false,
        string s => s.Length > 0,
        bool b => b,
        ICollection c => c.Count > 0,
        _ => true,
    };

    static string Format(object? value) => value switch
    {
        null => "",
        string s => s,
        double d => d.ToString(CultureInfo.InvariantCulture),
        IEnumerable items => Join(items),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    static string Join(IEnumerable items)
    {
        var text = new StringBuilder();
        foreach (var item in items)
        {
            if (text.Length > 0) text.Append(", ");
            text.Append(Format(item));
        }
        return text.ToString();
    }

    [GeneratedRegex(@"\{\{#(\w+)\}\}(.*?)\{\{/\1\}\}", RegexOptions.Singleline)]
    private static partial Regex Section();

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex Field();
}
