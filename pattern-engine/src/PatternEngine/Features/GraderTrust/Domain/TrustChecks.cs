using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Grading.Domain;
using PatternEngine.Features.Grading.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;
using PatternEngine.Plugins.Yaml;

namespace PatternEngine.Features.GraderTrust.Domain;

public sealed record TrustContext(IDomainPack Pack, string PackDir, PatternCatalog Catalog, IReadOnlyList<Case> Cases, TrustSettings Settings);

public sealed record TrustSettings(double ReferencePassRate, double MutationCatchRate, double RightReasonRate, IReadOnlyList<string>? Mutations);

public sealed record TrustLine(bool Ok, string Text);

public sealed record TrustResult(string Check, IReadOnlyList<TrustLine> Lines, IReadOnlyDictionary<string, int> Counts)
{
    public bool Ok => Lines.All(l => l.Ok);
}

/// <summary>
/// One test of the graders, with no model. The checks that run come from the settings (grader_trust.checks).
/// </summary>
public interface ITrustCheck
{
    string Name { get; }

    TrustResult Run(TrustContext context);
}

/// <summary>Grades one output text with the output graders. Returns the failed graders and their first failure.</summary>
public static class OutputGrading
{
    public static IReadOnlyDictionary<string, string> Failed(string text, Expect expect, IDomainPack pack, PatternCatalog catalog)
    {
        var parsed = pack.Adapter.Parse(text);
        var failed = new Dictionary<string, string>(StringComparer.Ordinal);
        var s = StructureGrader.Check(parsed, catalog, pack.Adapter);
        if (!s.Ok)
        {
            failed["structure"] = s.Failures[0];
        }

        var rules = pack.Adapter.Lint(parsed);
        if (rules.Count > 0)
        {
            failed["rules"] = rules[0];
        }

        var built = parsed.Roots.Count > 0 ? BuiltGrader.Check(StructureGrader.Built(parsed.Roots[0], catalog, pack.Adapter), expect) : Verdict.From(["no top pattern"]);
        if (!built.Ok)
        {
            failed["built"] = built.Failures[0];
        }

        var input = new GradeInput
        {
            Case = new Case { Id = "trust", Source = "trust", Tags = [], Request = string.Empty, Expect = expect },
            Catalog = catalog,
            Pack = pack,
            OutputText = text,
            Parsed = parsed,
            Built = parsed.Roots.Count > 0 ? StructureGrader.Built(parsed.Roots[0], catalog, pack.Adapter) : null,
        };
        foreach (var grader in pack.Graders)
        {
            var v = grader.Grade(input);
            if (!v.Ok)
            {
                failed[grader.Name] = v.Failures[0];
            }
        }

        return failed;
    }
}

/// <summary>Hand fixtures with the verdict that each grader must give (fixtures/expect.yaml).</summary>
public sealed class FixtureCheck : ITrustCheck
{
    public string Name => "fixtures";

    public TrustResult Run(TrustContext context)
    {
        var dir = Path.Combine(context.PackDir, "fixtures");
        var file = Path.Combine(dir, "expect.yaml");
        if (!File.Exists(file))
        {
            return new TrustResult(Name, [new TrustLine(true, "skip: no fixtures/expect.yaml")], new Dictionary<string, int>());
        }

        var lines = new List<TrustLine>();
        foreach (var e in Node.List(Yaml.Parse(File.ReadAllText(file))).Select(Node.Map))
        {
            var name = e.Get("file")!;
            var parsed = context.Pack.Adapter.Parse(File.ReadAllText(Path.Combine(dir, name)));
            var s = StructureGrader.Check(parsed, context.Catalog, context.Pack.Adapter);
            var r = context.Pack.Adapter.Lint(parsed);
            var wantStructure = e.Get("structure") == "true";
            var wantRules = e.Get("rules") == "true";
            lines.Add(new TrustLine(s.Ok == wantStructure, $"{name}: structure {Lower(s.Ok)} (want {Lower(wantStructure)}){(s.Ok ? string.Empty : " — " + s.Failures[0])}"));
            lines.Add(new TrustLine(r.Count == 0 == wantRules, $"{name}: rules {Lower(r.Count == 0)} (want {Lower(wantRules)}){(r.Count == 0 ? string.Empty : " — " + r[0])}"));
            if (TreeNode.From(e.Raw("tree")) is { } tree)
            {
                var b = BuiltGrader.Check(StructureGrader.Built(parsed.Roots[0], context.Catalog, context.Pack.Adapter), new Expect { Outcome = Outcomes.Match, Tree = tree });
                lines.Add(new TrustLine(b.Ok, $"{name}: built tree matches{(b.Ok ? string.Empty : " — " + b.Failures[0])}"));
            }

            // A fixture can also state the verdict of a grader of the pack, for example behavior.
            foreach (var grader in context.Pack.Graders.Where(g => e.ContainsKey(g.Name)))
            {
                var expect = new Expect { Outcome = Outcomes.Match, Tree = TreeNode.From(e.Raw("tree")), Spec = e.Get("spec") };
                var input = new GradeInput
                {
                    Case = new Case { Id = name, Source = "fixture", Tags = [], Request = string.Empty, Expect = expect },
                    Catalog = context.Catalog,
                    Pack = context.Pack,
                    Parsed = parsed,
                };
                var v = grader.Grade(input);
                var want = e.Get(grader.Name) == "true";
                lines.Add(new TrustLine(v.Ok == want, $"{name}: {grader.Name} {Lower(v.Ok)} (want {Lower(want)}){(v.Ok ? string.Empty : " — " + v.Failures[0])}"));
            }
        }

        return new TrustResult(Name, lines, new Dictionary<string, int> { ["lines"] = lines.Count });
    }

    private static string Lower(bool b) => b ? "true" : "false";
}

/// <summary>Known good: the pack draws each expected tree, and the graders must pass it.</summary>
public sealed class ReferenceCheck : ITrustCheck
{
    public string Name => "references";

    public TrustResult Run(TrustContext context)
    {
        if (context.Pack.Reference is null)
        {
            return new TrustResult(Name, [new TrustLine(true, "skip: the pack has no reference renderer")], new Dictionary<string, int>());
        }

        int n = 0, pass = 0;
        var failures = new List<string>();
        foreach (var c in context.Cases.Where(c => c.Expect is { Outcome: Outcomes.Match, Tree: not null }))
        {
            var output = context.Pack.Reference.Render(c.Expect.Tree!, context.Catalog);
            if (output is null)
            {
                continue;
            }

            n++;
            var failed = OutputGrading.Failed(output, c.Expect, context.Pack, context.Catalog);
            if (failed.Count == 0)
            {
                pass++;
            }
            else
            {
                failures.Add($"{c.Id}: {failed.First().Key}: {failed.First().Value}");
            }
        }

        var rate = n == 0 ? 1 : (double)pass / n;
        var lines = failures.Take(5).Select(f => new TrustLine(false, $"reference fails: {f}")).ToList();
        lines.Add(new TrustLine(rate >= context.Settings.ReferencePassRate, $"known good: {Pct(pass, n)} of reference outputs pass (target {context.Settings.ReferencePassRate * 100}%)"));
        return new TrustResult(Name, lines, new Dictionary<string, int> { ["references"] = n, ["pass"] = pass });
    }

    internal static string Pct(int a, int b) => $"{(b == 0 ? 100 : Math.Round(100.0 * a / b, MidpointRounding.AwayFromZero))}% ({a}/{b})";
}

/// <summary>Known bad: each operator breaks each reference output in one way. The graders must fail it, in the grader that the operator names.</summary>
public sealed class MutationCheck : ITrustCheck
{
    public string Name => "mutations";

    public TrustResult Run(TrustContext context)
    {
        if (context.Pack.Reference is null || context.Pack.Mutations.Count == 0)
        {
            return new TrustResult(Name, [new TrustLine(true, "skip: the pack has no reference renderer or no operators")], new Dictionary<string, int>());
        }

        var ops = context.Settings.Mutations is { } only ? context.Pack.Mutations.Where(o => only.Contains(o.Id)).ToList() : context.Pack.Mutations.ToList();
        var stats = ops.ToDictionary(o => o.Id, _ => new OpStats(), StringComparer.Ordinal);
        foreach (var c in context.Cases.Where(c => c.Expect is { Outcome: Outcomes.Match, Tree: not null }))
        {
            var output = context.Pack.Reference.Render(c.Expect.Tree!, context.Catalog);
            if (output is null)
            {
                continue;
            }

            foreach (var op in ops)
            {
                var broken = op.Apply(new MutationInput(output, c.Expect.Tree!, context.Pack.Adapter));
                if (broken is null || broken == output)
                {
                    continue;
                }

                var s = stats[op.Id];
                s.Applied++;
                var names = OutputGrading.Failed(broken, c.Expect, context.Pack, context.Catalog).Keys.ToList();
                if (names.Count == 0)
                {
                    s.Missed.Add(c.Id);
                    continue;
                }

                s.Caught++;
                if (op.Expect.Any(names.Contains))
                {
                    s.Right++;
                }
                else
                {
                    s.Wrong.Add($"{c.Id} (failed in {string.Join(", ", names)})");
                }
            }
        }

        var lines = new List<TrustLine>();
        foreach (var op in ops)
        {
            var s = stats[op.Id];
            var note = (s.Missed.Count > 0 ? $"   MISSED e.g. {s.Missed[0]}" : string.Empty) + (s.Wrong.Count > 0 ? $"   WRONG REASON e.g. {s.Wrong[0]}" : string.Empty);
            lines.Add(new TrustLine(true, $"  {op.Id,-19} applied {s.Applied,4}  caught {s.Caught,4}  right reason {s.Right,4}{note}"));
        }

        int applied = stats.Values.Sum(s => s.Applied), caught = stats.Values.Sum(s => s.Caught), right = stats.Values.Sum(s => s.Right);
        double Rate(int a) => applied == 0 ? 1 : (double)a / applied;
        lines.Add(new TrustLine(Rate(caught) >= context.Settings.MutationCatchRate, $"known bad: {ReferenceCheck.Pct(caught, applied)} of mutations caught (target {context.Settings.MutationCatchRate * 100}%)"));
        lines.Add(new TrustLine(Rate(right) >= context.Settings.RightReasonRate, $"known bad: {ReferenceCheck.Pct(right, applied)} caught by the grader that the mutation names (target {context.Settings.RightReasonRate * 100}%)"));
        return new TrustResult(Name, lines, new Dictionary<string, int> { ["applied"] = applied, ["caught"] = caught, ["right"] = right });
    }

    private sealed class OpStats
    {
        public int Applied { get; set; }

        public int Caught { get; set; }

        public int Right { get; set; }

        public List<string> Missed { get; } = [];

        public List<string> Wrong { get; } = [];
    }
}
