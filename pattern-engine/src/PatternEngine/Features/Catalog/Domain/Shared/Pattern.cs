namespace PatternEngine.Features.Catalog.Domain.Shared;

public enum PatternKind
{
    Leaf,
    Object,
    Group,
}

public enum PatternStatus
{
    Proposed,
    Trial,
    Stable,
}

public sealed record Variant(string Id, string When, string? Phrase);

/// <summary>A child part of a pattern. Count is "1", "0..4" or "1..n". Only limits the part to one variant.</summary>
public sealed record PartSpec(string Ref, string Count, string? Only);

/// <summary>A do_not_use_when rule: when the condition is true, use another pattern, or "no-fit".</summary>
public sealed record InsteadRule(string When, string Use)
{
    public const string NoFit = "no-fit";
}

/// <summary>
/// A param is a value that the request sets, for example a color. A required param has no default:
/// if the request does not give it, the engine asks.
/// </summary>
public sealed record ParamDef(string Key, string? Part, string? Attr, string? Default, string? Phrase, IReadOnlyList<string> Values, bool Required);

/// <summary>A typed port: data that flows into or out of a pattern, for domains with behavior.</summary>
public sealed record Port(string Name, string Type);

public sealed record Pattern(
    string Id,
    PatternKind Kind,
    int Level,
    PatternStatus Status,
    string? Phrase,
    string? Primitive,
    IReadOnlyList<string> UseWhen,
    IReadOnlyList<InsteadRule> DoNotUseWhen,
    IReadOnlyList<Variant> Variants,
    IReadOnlyList<PartSpec> Parts,
    IReadOnlyList<ParamDef> Params,
    IReadOnlyList<string> Related,
    IReadOnlyList<string> Asserts,
    IReadOnlyList<Port> Inputs,
    IReadOnlyList<Port> Outputs,
    IReadOnlyDictionary<string, string> Sections,
    IReadOnlyDictionary<string, object?> Extra,
    string File,
    string Hash,
    IReadOnlyList<string> ParseErrors)
{
    public IEnumerable<PartSpec> PartsFor(string? variant) => Parts.Where(p => p.Only is null || p.Only == variant);

    public ParamDef? Param(string key) => Params.FirstOrDefault(p => p.Key == key);

    public IEnumerable<string> VariantIds => Variants.Select(v => v.Id);
}

public readonly record struct PartCount(int Min, int Max)
{
    public const int Unbounded = int.MaxValue;

    public static bool TryParse(string spec, out PartCount count)
    {
        count = default;
        var match = System.Text.RegularExpressions.Regex.Match(spec, @"^(\d+)(?:\.\.(\d+|n))?$");
        if (!match.Success)
        {
            return false;
        }

        var min = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var max = !match.Groups[2].Success ? min : match.Groups[2].Value == "n" ? Unbounded : int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        count = new PartCount(min, max);
        return true;
    }

    public static PartCount Parse(string spec) => TryParse(spec, out var c) ? c : throw new FormatException($"bad count \"{spec}\"");

    public bool Allows(int n) => n >= Min && n <= Max;
}
