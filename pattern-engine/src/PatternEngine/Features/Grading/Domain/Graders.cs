using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Grading.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;

namespace PatternEngine.Features.Grading.Domain;

/// <summary>Compares what the engine says it selected (the trace) with the case.</summary>
public sealed class SelectionGrader : IGrader
{
    public string Name => "selection";

    public Verdict Grade(GradeInput input) => Check(input.Trace, input.Case.Expect);

    public static Verdict Check(Trace.Domain.Shared.RunTrace? trace, Expect expect)
    {
        if (trace is null)
        {
            return Verdict.From(["no trace"]);
        }

        var failures = new List<string>();
        switch (expect.Outcome)
        {
            case Outcomes.NoFit:
                if (trace.Outcome != Outcomes.NoFit)
                {
                    failures.Add($"expected outcome \"no-fit\", got \"{trace.Outcome}\"");
                }

                var nearest = expect.Nearest ?? [];
                if (nearest.Count > 0 && !nearest.Contains(trace.Nearest ?? string.Empty))
                {
                    failures.Add($"expected nearest {string.Join(" or ", nearest)}, got \"{trace.Nearest ?? "-"}\"");
                }

                if (trace.Deltas.Count == 0)
                {
                    failures.Add("no deltas listed");
                }

                break;
            case Outcomes.Ask:
                if (trace.Outcome != Outcomes.Ask)
                {
                    failures.Add($"expected outcome \"ask\", got \"{trace.Outcome}\"");
                }

                break;
            default:
                if (trace.Outcome != Outcomes.Match)
                {
                    failures.Add($"expected outcome \"match\", got \"{trace.Outcome}\"");
                }

                if (expect.Tree is not null)
                {
                    failures.AddRange(TreeNode.Diff(expect.Tree, trace.Tree));
                }

                if (expect.Remainder is { } remainder)
                {
                    var want = string.Join(", ", remainder.Order(StringComparer.Ordinal));
                    var got = string.Join(", ", trace.Remainder.Select(r => r.Outcome).Order(StringComparer.Ordinal));
                    if (want != got)
                    {
                        failures.Add($"expected remainder [{want}], got [{got}]");
                    }
                }

                break;
        }

        return Verdict.From(failures);
    }
}

/// <summary>Compares the pattern tree that the output really has with the expected tree.</summary>
public sealed class BuiltGrader : IGrader
{
    public string Name => "built";

    public Verdict Grade(GradeInput input) => Check(input.Built, input.Case.Expect);

    public static Verdict Check(TreeNode? built, Expect expect)
    {
        if (expect.Outcome != Outcomes.Match || expect.Tree is null)
        {
            return Verdict.Skip;
        }

        return built is null ? Verdict.From(["no top pattern"]) : Verdict.From(TreeNode.Diff(expect.Tree, built));
    }
}

/// <summary>Checks the output against the pattern definitions: known patterns, variants, part counts, leaf form and asserts.</summary>
public sealed class StructureGrader : IGrader
{
    public string Name => "structure";

    public Verdict Grade(GradeInput input)
    {
        if (input.Case.Expect.Outcome == Outcomes.Ask && input.Parsed is null)
        {
            return Verdict.Skip;
        }

        return input.Parsed is null ? Verdict.From(["no output"]) : Check(input.Parsed, input.Catalog, input.Pack.Adapter, input.ProposedId);
    }

    public static Verdict Check(ParsedOutput parsed, PatternCatalog catalog, IOutputAdapter adapter, string? proposedId = null)
    {
        var failures = new List<string>();
        void Fail(string where, string message) => failures.Add($"{where}: {message}");
        foreach (var e in parsed.Errors)
        {
            Fail("parse", e);
        }

        if (parsed.Roots.Count == 0)
        {
            Fail("output", "no pattern found");
        }

        if (parsed.Roots.Count > 1)
        {
            Fail("output", $"expected one top pattern, got {parsed.Roots.Count}");
        }

        void Walk(OutputNode node, string where)
        {
            var here = where.Length > 0 ? $"{where} > {node.Pattern}" : node.Pattern;
            if (where.Length == 0 && node.Pattern == Solution.Id)
            {
                // A solution is not a pattern: each of its parts is a complete pattern. The pack graders check the wires.
                foreach (var c in node.Children)
                {
                    Walk(c, here);
                }

                return;
            }

            var def = catalog.Get(node.Pattern);
            if (def is null)
            {
                if (node.Pattern == proposedId)
                {
                    foreach (var c in node.Children)
                    {
                        Walk(c, here);
                    }

                    return;
                }

                Fail(here, $"unknown pattern \"{node.Pattern}\"");
                return;
            }

            if (def.Kind == PatternKind.Leaf)
            {
                if (node.Children.Count > 0)
                {
                    Fail(here, "a leaf part has child patterns");
                }

                foreach (var m in adapter.CheckLeaf(node, def))
                {
                    Fail(here, m);
                }

                return;
            }

            var ids = def.VariantIds.ToList();
            if (!ids.Contains(node.Variant ?? string.Empty))
            {
                Fail(here, $"variant \"{node.Variant ?? "-"}\" is not one of {string.Join(", ", ids)}");
            }

            var specs = def.PartsFor(node.Variant).ToList();
            var allowed = specs.Select(s => s.Ref).ToHashSet();
            foreach (var c in node.Children.Where(c => !allowed.Contains(c.Pattern)))
            {
                Fail(here, $"part \"{c.Pattern}\" is not allowed here");
            }

            foreach (var s in specs)
            {
                var count = PartCount.Parse(s.Count);
                var n = node.Children.Count(c => c.Pattern == s.Ref);
                if (!count.Allows(n))
                {
                    Fail(here, $"part \"{s.Ref}\": count {n}, allowed {s.Count}");
                }
            }

            foreach (var r in adapter.Assert(node, def, catalog).Where(r => !r.Ok))
            {
                Fail(here, $"assert \"{r.Assert}\" failed{(string.IsNullOrEmpty(r.Detail) ? string.Empty : $" ({r.Detail})")}");
            }

            foreach (var c in node.Children)
            {
                Walk(c, here);
            }
        }

        foreach (var r in parsed.Roots)
        {
            Walk(r, string.Empty);
        }

        return Verdict.From(failures);
    }

    /// <summary>The pattern tree of an output, without leaf parts. The adapter reads the params.</summary>
    public static TreeNode Built(OutputNode node, PatternCatalog catalog, IOutputAdapter adapter)
    {
        var def = catalog.Get(node.Pattern);
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in def?.Params ?? [])
        {
            var v = adapter.ReadParam(node, p);
            if (v is not null)
            {
                parameters[p.Key] = v;
            }
        }

        var kids = node.Children.Where(c => catalog.Get(c.Pattern) is { Kind: not PatternKind.Leaf }).ToList();
        return new TreeNode
        {
            P = node.Pattern,
            V = node.Variant,
            Params = parameters.Count > 0 ? parameters : null,
            Parts = kids.Count > 0 ? kids.Select(k => Built(k, catalog, adapter)).ToList() : null,
        };
    }
}

/// <summary>The subject rules of the pack (the lint of the adapter).</summary>
public sealed class RulesGrader : IGrader
{
    public string Name => "rules";

    public Verdict Grade(GradeInput input)
    {
        if (input.Case.Expect.Outcome == Outcomes.Ask && input.Parsed is null)
        {
            return Verdict.Skip;
        }

        return input.Parsed is null ? Verdict.From(["no output"]) : Verdict.From(input.Pack.Adapter.Lint(input.Parsed));
    }
}

/// <summary>A no-fit case must leave a valid proposed pattern that names the nearest pattern.</summary>
public sealed class ProposedGrader : IGrader
{
    public string Name => "proposed";

    public Verdict Grade(GradeInput input)
    {
        if (input.Case.Expect.Outcome != Outcomes.NoFit)
        {
            return Verdict.Skip;
        }

        var p = input.ProposedId is null ? null : input.Catalog.Get(input.ProposedId);
        if (p is null)
        {
            return Verdict.From(["no proposed pattern file"]);
        }

        var failures = new List<string>();
        if (p.Status != PatternStatus.Proposed)
        {
            failures.Add($"status is \"{p.Status.ToString().ToLowerInvariant()}\", not \"proposed\"");
        }

        if (!p.Related.Contains(input.Trace?.Nearest ?? string.Empty))
        {
            failures.Add("related does not name the nearest pattern");
        }

        return Verdict.From(failures);
    }
}
