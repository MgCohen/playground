using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;
using PatternEngine.Features.Trace.Domain.Shared;

namespace PatternEngine.Features.Grading.Domain.Shared;

/// <summary>Everything a grader can read about one run. The grader never runs the engine.</summary>
public sealed record GradeInput
{
    public required Case Case { get; init; }

    public required PatternCatalog Catalog { get; init; }

    public required IDomainPack Pack { get; init; }

    public RunTrace? Trace { get; init; }

    public string? OutputText { get; init; }

    public ParsedOutput? Parsed { get; init; }

    /// <summary>The pattern tree read from the output, without leaf parts.</summary>
    public TreeNode? Built { get; init; }

    /// <summary>The id of the proposed pattern of a no-fit run, if any.</summary>
    public string? ProposedId { get; init; }
}

public sealed record Verdict(bool Ok, IReadOnlyList<string> Failures, bool Skipped = false)
{
    public static Verdict Pass { get; } = new(true, []);

    public static Verdict Skip { get; } = new(true, [], true);

    public static Verdict From(IReadOnlyList<string> failures) => new(failures.Count == 0, failures);
}

/// <summary>
/// One deterministic check of one run. A grader returns Skip when the case is not for it.
/// The graders that run come from the settings (grading.graders), so a grader can be added or removed without code changes.
/// </summary>
public interface IGrader
{
    string Name { get; }

    Verdict Grade(GradeInput input);
}
