using System.Text.Json.Serialization;
using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Grading.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;
using PatternEngine.Features.Trace.Domain.Shared;

namespace PatternEngine.Features.Grading.Domain;

public sealed record GraderOutcome(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("skipped")] bool Skipped,
    [property: JsonPropertyName("failures")] IReadOnlyList<string> Failures);

public sealed record GradeReport(
    [property: JsonPropertyName("verdicts")] IReadOnlyDictionary<string, GraderOutcome> Verdicts,
    [property: JsonPropertyName("pass")] bool Pass,
    [property: JsonPropertyName("consistent")] bool Consistent);

/// <summary>All graders that a run can use: the core graders and the graders of the pack. The settings select the ones that run.</summary>
public sealed class GraderSet(IEnumerable<IGrader> core)
{
    private readonly List<IGrader> core = core.ToList();

    public IReadOnlyList<IGrader> Select(IReadOnlyList<string> names, IDomainPack pack)
    {
        var all = core.Concat(pack.Graders).GroupBy(g => g.Name).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var missing = names.Where(n => !all.ContainsKey(n)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"no grader {string.Join(", ", missing)} (graders: {string.Join(", ", all.Keys)})");
        }

        return names.Select(n => all[n]).ToList();
    }

    public static GraderSet Default { get; } = new([new SelectionGrader(), new BuiltGrader(), new StructureGrader(), new RulesGrader(), new ProposedGrader(), new QuestionsGrader()]);
}

/// <summary>Grades one run: it reads the output with the adapter, builds the input once, and runs each grader.</summary>
public static class RunGrading
{
    public static GradeReport Grade(Case c, IDomainPack pack, PatternCatalog catalog, RunTrace? trace, string? outputText, IReadOnlyList<IGrader> graders)
    {
        ParsedOutput? parsed = string.IsNullOrEmpty(outputText) ? null : pack.Adapter.Parse(outputText);
        var built = parsed?.Roots.Count > 0 ? StructureGrader.Built(parsed.Roots[0], catalog, pack.Adapter) : null;
        var proposedId = trace?.Proposed is { } proposed ? Path.GetFileNameWithoutExtension(proposed) : null;
        var input = new GradeInput { Case = c, Catalog = catalog, Pack = pack, Trace = trace, OutputText = outputText, Parsed = parsed, Built = built, ProposedId = proposedId };
        var verdicts = new Dictionary<string, GraderOutcome>(StringComparer.Ordinal);
        foreach (var g in graders)
        {
            Verdict v;
            try
            {
                v = g.Grade(input);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                v = Verdict.From([$"grader error: {e.Message}"]);
            }

            verdicts[g.Name] = new GraderOutcome(v.Ok, v.Skipped, v.Failures);
        }

        var consistent = trace?.Tree is not null && built is not null && TreeNode.Diff(built.Shape(), trace.Tree.Shape()).Count == 0;
        return new GradeReport(verdicts, verdicts.Values.All(v => v.Ok), consistent);
    }
}
