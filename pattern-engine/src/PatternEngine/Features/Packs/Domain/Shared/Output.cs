using PatternEngine.Features.Catalog.Domain.Shared;

namespace PatternEngine.Features.Packs.Domain.Shared;

/// <summary>One pattern instance in an output. A pack can subclass it to keep its own data, for example a shape and its box.</summary>
public class OutputNode(string pattern, string? variant)
{
    public string Pattern { get; } = pattern;

    public string? Variant { get; } = variant;

    public List<OutputNode> Children { get; } = [];
}

public sealed record ParsedOutput(IReadOnlyList<OutputNode> Roots, IReadOnlyList<string> Errors, object? Data = null);

public sealed record AssertResult(string Assert, bool Ok, string? Detail = null, bool Skipped = false);

/// <summary>
/// Reads one output file of a domain. The core calls only these methods, so nothing in the core knows the output format.
/// </summary>
public interface IOutputAdapter
{
    ParsedOutput Parse(string text);

    /// <summary>The subject rules that apply to any output, for example the canvas or the allowed shapes.</summary>
    IReadOnlyList<string> Lint(ParsedOutput parsed);

    IReadOnlyList<AssertResult> Assert(OutputNode node, Pattern pattern, PatternCatalog catalog);

    IReadOnlyList<string> CheckLeaf(OutputNode node, Pattern leaf);

    string? ReadParam(OutputNode node, ParamDef param);
}
