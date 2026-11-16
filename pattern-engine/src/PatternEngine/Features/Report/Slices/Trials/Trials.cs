using System.Text.Json;
using PatternEngine.Features.Eval.Domain;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Features.Report.Domain;
using PatternEngine.Plugins.Messages;

namespace PatternEngine.Features.Report.Slices.Trials;

public sealed record TrialsCommand(string? Domain) : ICommand<TrialsResult>;

public sealed record TrialsResult(IReadOnlyList<TrialMetric> Metrics, IReadOnlyList<string> Notifications, string File);

/// <summary>Reads all result files of a pack, computes the trial metrics, and writes a notification for each threshold.</summary>
public sealed class TrialsHandler(PackRegistry packs) : IHandler<TrialsCommand, TrialsResult>
{
    public Task<Result<TrialsResult>> HandleAsync(TrialsCommand command, CancellationToken ct)
    {
        var pack = packs.Load(command.Domain);
        var dir = packs.Root.Path(pack.Settings.Section("paths").Text("results"));
        var results = Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.json").Select(Read).OfType<EvalResult>().Where(r => r.Domain == pack.Pack.Name).ToList()
            : [];
        var metrics = TrialMetrics.Of(pack.Catalog, results, PromotionSettings.From(pack.Settings));
        var file = packs.Root.Path(pack.Settings.Section("promotion").Text("notifications"));
        var added = TrialMetrics.Notify(metrics, file, DateTime.UtcNow);
        return Task.FromResult(Result<TrialsResult>.Ok(new TrialsResult(metrics, added, file)));
    }

    private static EvalResult? Read(string file)
    {
        try
        {
            var r = JsonSerializer.Deserialize<EvalResult>(File.ReadAllText(file));
            return r?.Engine == "csharp-hybrid" ? r : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
