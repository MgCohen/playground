using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Eval.Domain;
using PatternEngine.Plugins.Settings;

namespace PatternEngine.Features.Report.Domain;

public sealed record PromotionSettings(int MinRuns, double GoodPassRate, double BadPassRate)
{
    public static PromotionSettings From(Settings settings)
    {
        var p = settings.Section("promotion");
        return new PromotionSettings(p.Int("min_runs"), p.Number("good_pass_rate"), p.Number("bad_pass_rate"));
    }
}

public static class TrialStates
{
    public const string Collecting = "collecting";
    public const string ReadyForStable = "ready for stable";
    public const string Failing = "failing";
}

public sealed record TrialMetric(string Id, string Hash, int Runs, int Passes, string State)
{
    public double Rate => Runs == 0 ? 0 : (double)Passes / Runs;
}

/// <summary>
/// The metrics of each trial pattern, from all result files: the runs that used the current version of the pattern, and how many passed.
/// A run of an older version does not count, so an edit restarts the metrics.
/// </summary>
public static class TrialMetrics
{
    public static IReadOnlyList<TrialMetric> Of(PatternCatalog catalog, IEnumerable<EvalResult> results, PromotionSettings settings)
    {
        var runs = results.SelectMany(r => r.Runs).ToList();
        return catalog.All.Where(p => p.Status == PatternStatus.Trial).Select(p =>
        {
            var used = runs.Where(r => r.Trace?.Hashes?.Patterns.GetValueOrDefault(p.Id) == p.Hash).ToList();
            var passes = used.Count(r => r.Pass);
            var rate = used.Count == 0 ? 0 : (double)passes / used.Count;
            var state = used.Count < settings.MinRuns ? TrialStates.Collecting
                : rate >= settings.GoodPassRate ? TrialStates.ReadyForStable
                : rate < settings.BadPassRate ? TrialStates.Failing
                : TrialStates.Collecting;
            return new TrialMetric(p.Id, p.Hash, used.Count, passes, state);
        }).ToList();
    }

    /// <summary>Adds one line to the notification file for each pattern that reached a threshold, once for each version and state.</summary>
    public static IReadOnlyList<string> Notify(IReadOnlyList<TrialMetric> metrics, string file, DateTime now)
    {
        var existing = File.Exists(file) ? File.ReadAllText(file) : "# Notifications\n\nA trial pattern reached a threshold. A person makes the final call: `pe stable <id>` or `pe remove <id>`.\n\n";
        var added = new List<string>();
        foreach (var m in metrics.Where(m => m.State != TrialStates.Collecting))
        {
            var key = $"`{m.Id}` ({m.Hash}): {m.State}";
            if (existing.Contains(key, StringComparison.Ordinal))
            {
                continue;
            }

            var line = $"- {now:yyyy-MM-dd HH:mm} UTC {key}, {m.Passes}/{m.Runs} runs pass";
            existing += line + "\n";
            added.Add(line);
        }

        if (added.Count > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, existing);
        }

        return added;
    }
}
