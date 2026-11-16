using PatternEngine.Features.Compose.Domain.Shared;
using PatternEngine.Plugins.Settings;
using PatternEngine.Plugins.Yaml;

namespace PatternEngine.Domains.Party;

public sealed record EvalVerdict(string Rubric, bool Ok, string Reason);

/// <summary>The eval of one run: a verdict for each eval rubric of the rung, the measures beside them, and the pass.</summary>
public sealed record PartyEvalResult(IReadOnlyList<EvalVerdict> Verdicts, IReadOnlyDictionary<string, string> Measures, bool Pass)
{
    public EvalVerdict? Of(string rubric) => Verdicts.FirstOrDefault(v => v.Rubric == rubric);
}

/// <summary>The pass rule from party.pass: the rubrics in the gate, and the rule all or percent (with pass_at).</summary>
public sealed record PassRule(IReadOnlyList<string> Rubrics, string Rule, double PassAt)
{
    public static PassRule From(Settings party)
    {
        var pass = party.Section("pass");
        return new PassRule(pass.List("rubrics"), pass.Text("rule"), pass.Has("pass_at") ? pass.Number("pass_at") : 1.0);
    }

    public bool Pass(IReadOnlyList<EvalVerdict> verdicts)
    {
        var gate = verdicts.Where(v => Rubrics.Contains(v.Rubric)).ToList();
        return Rule switch
        {
            "all" => gate.All(v => v.Ok),
            "percent" => gate.Count == 0 || (double)gate.Count(v => v.Ok) / gate.Count >= PassAt,
            _ => throw new SettingsException($"party.pass.rule \"{Rule}\" must be all or percent"),
        };
    }
}

/// <summary>
/// The eval of the party domain. The pass uses the oracle truth of the committed parties, never the verdicts of the run.
/// </summary>
public sealed class PartyEval(PartyPack pack)
{
    private readonly Oracle oracle = new(pack.Heroes);

    public Oracle Oracle => oracle;

    /// <summary>Grades the commits of a run: one party (or null for "none") for each quest of the case, in order.</summary>
    public PartyEvalResult Grade(PartyCase c, IReadOnlyList<IReadOnlyList<string>?> commits, Anchor? decomposed = null)
    {
        if (commits.Count != c.Quests.Count)
        {
            throw new InvalidOperationException($"case {c.Id}: {commits.Count} commits for {c.Quests.Count} quests");
        }

        var best = oracle.BestAssignment(c);
        var none = best.Total is null;
        var measures = new Dictionary<string, string>(StringComparer.Ordinal);
        var (valid, why) = Valid(c, commits, none);
        var total = commits.OfType<IReadOnlyList<string>>().Sum(oracle.Cost);
        measures["cost"] = commits.All(p => p is null) ? "none" : total.ToString(System.Globalization.CultureInfo.InvariantCulture);
        measures["best cost"] = none ? "none" : best.Total!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var verdicts = new List<EvalVerdict>();
        foreach (var rubric in pack.Eval.Rubrics.Where(r => Node.Strings(r.Args.Raw("rungs")).Contains(c.Rung)))
        {
            switch (rubric.Id)
            {
                case "valid":
                    verdicts.Add(new EvalVerdict("valid", valid, why));
                    break;
                case "smallest":
                    verdicts.Add(Smallest(c, commits, valid, none, measures));
                    break;
                case "best-cost":
                case "best-total":
                    var ok = valid && (none || total == best.Total);
                    verdicts.Add(new EvalVerdict(rubric.Id, ok, !valid ? "the party is not valid" : none ? "none is correct" : $"cost {total}, best {best.Total}"));
                    break;
                case "spec" when decomposed is not null:
                    verdicts.Add(Spec(c.Anchor, decomposed, measures));
                    break;
                case "spec":
                    break;
                default:
                    throw new InvalidOperationException($"the eval anchor has an unknown rubric \"{rubric.Id}\"");
            }
        }

        return new PartyEvalResult(verdicts, measures, PassRule.From(pack.Settings.Section("party")).Pass(verdicts));
    }

    /// <summary>Compares decomposed rubrics with the hidden rubrics by their args: a tag, a stat or the budget, and an amount.</summary>
    public static (int Missed, int Extra, int Wrong) Compare(Anchor hidden, Anchor decomposed)
    {
        var want = hidden.Rubrics.Select(Key).ToDictionary(k => k.Key, k => k.Amount, StringComparer.Ordinal);
        var got = decomposed.Rubrics.Select(Key).GroupBy(k => k.Key).ToDictionary(g => g.Key, g => g.First().Amount, StringComparer.Ordinal);
        return (want.Keys.Count(k => !got.ContainsKey(k)), got.Keys.Count(k => !want.ContainsKey(k)), want.Count(e => got.TryGetValue(e.Key, out var a) && a != e.Value));
    }

    private (bool Ok, string Why) Valid(PartyCase c, IReadOnlyList<IReadOnlyList<string>?> commits, bool none)
    {
        if (none)
        {
            return commits.All(p => p is null) ? (true, "none is correct") : (false, "no valid party exists, but the run committed one");
        }

        var problems = new List<string>();
        for (var i = 0; i < c.Quests.Count; i++)
        {
            var quest = c.Quests[i];
            if (commits[i] is not { } party)
            {
                problems.Add($"{quest.Id}: none, but a valid party exists");
                continue;
            }

            if (oracle.Resolve(party) is null)
            {
                problems.Add($"{quest.Id}: unknown or repeated hero in [{string.Join(", ", party)}]");
                continue;
            }

            problems.AddRange(oracle.Failed(quest, party).Select(r => $"{quest.Id}: {r.Id} is false"));
        }

        problems.AddRange(c.CaseRubrics.Where(r => !Oracle.PoolTruth(r, commits)).Select(r => $"{c.Id}: {r.Id} is false"));
        return problems.Count == 0 ? (true, "each rubric is true") : (false, string.Join("; ", problems));
    }

    private EvalVerdict Smallest(PartyCase c, IReadOnlyList<IReadOnlyList<string>?> commits, bool valid, bool none, Dictionary<string, string> measures)
    {
        if (none)
        {
            return new EvalVerdict("smallest", valid, valid ? "none is correct" : "the party is not valid");
        }

        var smallest = oracle.Best(c.Quests[0]).SmallestSize!.Value;
        var size = commits[0]?.Count ?? 0;
        measures["size"] = size.ToString(System.Globalization.CultureInfo.InvariantCulture);
        measures["smallest"] = smallest.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new EvalVerdict("smallest", valid && size == smallest, valid ? $"size {size}, smallest {smallest}" : "the party is not valid");
    }

    private static EvalVerdict Spec(Anchor hidden, Anchor decomposed, Dictionary<string, string> measures)
    {
        var (missed, extra, wrong) = Compare(hidden, decomposed);
        measures["spec"] = $"{missed} missed, {extra} extra, {wrong} wrong amount";
        return new EvalVerdict("spec", missed + extra + wrong == 0, measures["spec"]);
    }

    private static (string Key, string Amount) Key(Rubric r) =>
        r.Args.Get("tag") is { } tag ? ($"tag:{tag}", string.Empty)
        : r.Args.Get("stat") is { } stat ? ($"stat:{stat}", r.Args.Get("min") ?? string.Empty)
        : r.Args.Get("max_cost") is { } max ? ("max_cost", max)
        : r.Args.ContainsKey("unavailable") ? ("unavailable", string.Join(",", Node.Strings(r.Args.Raw("unavailable")).Order(StringComparer.Ordinal)))
        : ($"other:{r.Id}", string.Empty);
}
