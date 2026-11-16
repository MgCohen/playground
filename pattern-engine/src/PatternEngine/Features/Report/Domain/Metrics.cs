using System.Globalization;
using System.Text;
using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Eval.Domain;
using PatternEngine.Plugins.Settings;
using PatternEngine.Plugins.Yaml;

namespace PatternEngine.Features.Report.Domain;

/// <summary>One rate: n of d. The id is stable, so a target can name it.</summary>
public sealed record Metric(string Id, string Label, int N, int D)
{
    public double Rate => D == 0 ? 0 : (double)N / D;

    public string Text => D == 0 ? "—" : $"{Math.Round(100.0 * N / D, MidpointRounding.AwayFromZero)}% ({N}/{D})";
}

/// <summary>Turns one result into metrics. A metric that counts graders reads only the graders that ran.</summary>
public static class Metrics
{
    public static IReadOnlyList<Metric> Of(EvalResult result)
    {
        var runs = result.Runs;
        bool Ok(RunRecord r, string grader) => r.Grades.Verdicts.TryGetValue(grader, out var v) && v.Ok;
        bool Ran(RunRecord r, string grader) => r.Grades.Verdicts.TryGetValue(grader, out var v) && !v.Skipped;
        Metric Grader(string id, string label, string grader)
        {
            var ran = runs.Where(r => Ran(r, grader)).ToList();
            return new Metric(id, label, ran.Count(r => Ok(r, grader)), ran.Count);
        }

        var cases = runs.Select(r => r.Case).Distinct().ToList();
        var near = runs.Where(r => r.Tags.Contains("near-miss")).ToList();
        var nofit = runs.Where(r => r.Expect.Outcome == Outcomes.NoFit).ToList();
        var ask = runs.Where(r => r.Expect.Outcome == Outcomes.Ask || (r.Expect.Asks?.Count ?? 0) > 0).ToList();
        var plain = runs.Where(r => r.Hidden is null && (r.Expect.Asks?.Count ?? 0) == 0).ToList();
        return
        [
            Grader("selection", "selection accuracy", "selection"),
            Grader("built", "built tree matches", "built"),
            Grader("structure", "structure pass", "structure"),
            Grader("rules", "rules pass", "rules"),
            .. runs.SelectMany(r => r.Grades.Verdicts.Keys).Distinct().Where(g => g is not ("selection" or "built" or "structure" or "rules" or "proposed" or "questions"))
                .Select(g => Grader(g, $"{g} pass", g)),
            new Metric("pass@1", "pass@1 (all checks)", runs.Count(r => r.Pass), runs.Count),
            new Metric("pass^k", $"pass^{result.K} (all runs of a case)", cases.Count(id => runs.Where(r => r.Case == id).All(r => r.Pass)), cases.Count),
            new Metric("near-miss", "near-miss accuracy", near.Count(r => Ok(r, "selection")), near.Count),
            new Metric("no-fit-precision", "no-fit precision", nofit.Count(r => r.Trace?.Outcome == Outcomes.NoFit), nofit.Count),
            new Metric("no-fit", "no-fit full pass", nofit.Count(r => r.Pass), nofit.Count),
            new Metric("ask", "question cases pass", ask.Count(r => r.Pass), ask.Count),
            new Metric("extra-questions", "runs with an extra question", plain.Count(r => r.Trace?.Questions.Count > 0), plain.Count),
            new Metric("escalated", "runs with an escalation", runs.Count(r => r.Agent.Escalations > 0), runs.Count),
            new Metric("stopped", "runs stopped by a limit", runs.Count(r => r.Trace?.StopReason is not null), runs.Count),
            new Metric("agent-errors", "agent errors", runs.Count(r => r.Agent.Error is not null), runs.Count),
            new Metric("trace", "trace matches output", runs.Count(r => r.Grades.Consistent), runs.Count(r => r.Trace?.Tree is not null && r.Agent.Error is null)),
        ];
    }

    public sealed record TargetCheck(bool Ok, IReadOnlyList<string> Lines)
    {
        public string Text(string mode) => Lines.Count == 0
            ? $"Targets: none for mode \"{mode}\"."
            : $"Targets (mode {mode}): {(Ok ? "all met" : "BELOW TARGET")}\n" + string.Join("\n", Lines);
    }

    /// <summary>The targets of a mode: targets[mode], else targets.default. Null means no targets.</summary>
    public static TargetCheck CheckTargets(EvalResult result, Settings settings)
    {
        var targets = Node.Map(settings.Section("eval").Raw("targets"));
        var raw = targets.ContainsKey(result.Mode) ? targets.Raw(result.Mode) : targets.Raw("default");
        if (raw is null)
        {
            return new TargetCheck(true, []);
        }

        var metrics = Of(result).ToDictionary(m => m.Id, StringComparer.Ordinal);
        var lines = new List<string>();
        var ok = true;
        foreach (var (id, want) in Node.Map(raw))
        {
            if (!metrics.TryGetValue(id, out var m))
            {
                lines.Add($"  ?    {id}: unknown target id (use {string.Join(", ", metrics.Keys)})");
                continue;
            }

            if (m.D == 0)
            {
                lines.Add($"  -    {id}: no runs to measure");
                continue;
            }

            var max = Node.Map(want).Get("max");
            var limit = double.Parse(max ?? Node.Text(want)!, CultureInfo.InvariantCulture);
            var good = max is not null ? m.Rate <= limit : m.Rate >= limit;
            ok &= good;
            lines.Add($"  {(good ? "ok  " : "LOW ")} {id}: {m.Text}, target {(max is not null ? "<= " : ">= ")}{limit * 100}%");
        }

        return new TargetCheck(ok, lines);
    }

    public static string Report(EvalResult result, EvalResult? baseline = null)
    {
        var m = Of(result);
        var b = baseline is null ? null : Of(baseline).ToDictionary(x => x.Id);
        var runs = result.Runs;
        var n = Math.Max(1, runs.Count);
        var sb = new StringBuilder();
        sb.AppendLine($"Report {result.RunId}{(result.Ablate.Count > 0 ? $" (ablate {string.Join(",", result.Ablate)})" : string.Empty)}: {result.Cases} cases, {runs.Count} runs");
        sb.AppendLine($"engine {result.Engine}, mode {result.Mode}, model {result.Model}, k={result.K}, catalog {result.Catalog}, graders {string.Join(",", result.Graders)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"API-equivalent cost ${runs.Sum(r => r.Agent.Cost):0.00} (${runs.Sum(r => r.Agent.Cost) / n:0.000} per run), {runs.Sum(r => r.Agent.Ms) / 1000.0 / n:0} s per run, {(double)runs.Sum(r => r.Agent.Calls) / n:0.0} agent calls per run");
        sb.AppendLine();
        if (result.Mode != "full")
        {
            sb.AppendLine($"Note: a {result.Mode} run is a sample. Use --mode full to validate.").AppendLine();
        }

        sb.AppendLine($"{"metric",-34}{"score",-14}{(b is null ? string.Empty : "baseline")}");
        foreach (var x in m)
        {
            sb.AppendLine($"{x.Label,-34}{x.Text,-14}{(b?.GetValueOrDefault(x.Id)?.Text ?? string.Empty)}");
        }

        sb.AppendLine().AppendLine($"{"by top pattern",-34}pass@1");
        foreach (var g in runs.GroupBy(r => r.Expect.Outcome == Outcomes.Match ? r.Expect.Tree?.P ?? r.Expect.Spec ?? "-" : r.Expect.Outcome))
        {
            sb.AppendLine($"  {g.Key,-32}{new Metric(g.Key, g.Key, g.Count(r => r.Pass), g.Count()).Text}");
        }

        var fails = runs.SelectMany(r => r.Grades.Verdicts.Where(v => !v.Value.Ok).Select(v => $"{v.Key}: {System.Text.RegularExpressions.Regex.Replace(v.Value.Failures.FirstOrDefault() ?? string.Empty, @"\d+", "#")}"))
            .GroupBy(x => x).OrderByDescending(g => g.Count()).Take(8).ToList();
        if (fails.Count > 0)
        {
            sb.AppendLine().AppendLine("most common failures");
            foreach (var f in fails)
            {
                sb.AppendLine($"  {f.Count(),3}× {f.Key}");
            }
        }

        return sb.ToString();
    }
}
