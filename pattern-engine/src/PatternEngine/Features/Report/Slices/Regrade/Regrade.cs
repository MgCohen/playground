using System.Text.Json;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Eval.Domain;
using PatternEngine.Features.Grading.Domain;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Features.Report.Domain;
using PatternEngine.Plugins.Messages;

namespace PatternEngine.Features.Report.Slices.Regrade;

public sealed record RegradeCommand(string ResultFile) : ICommand<RegradeResult>;

public sealed record RegradeResult(string RunId, int PassBefore, int PassNow, IReadOnlyList<string> Changed);

/// <summary>Grades the stored outputs of a result again with the current patterns and graders. No agent runs.</summary>
public sealed class RegradeHandler(PackRegistry packs, GraderSet graders) : IHandler<RegradeCommand, RegradeResult>
{
    public async Task<Result<RegradeResult>> HandleAsync(RegradeCommand command, CancellationToken ct)
    {
        var result = JsonSerializer.Deserialize<EvalResult>(await File.ReadAllTextAsync(command.ResultFile, ct))!;
        var pack = packs.Load(result.Domain);
        var runs = Path.Combine(packs.Root.Path(pack.Settings.Section("paths").Text("runs")), result.RunId);
        var gradersToRun = graders.Select(pack.Settings.Section("grading").List("graders"), pack.Pack);
        int before = 0, now = 0;
        var changed = new List<string>();
        foreach (var r in result.Runs)
        {
            var dir = Path.Combine(runs, $"{r.Case}-{r.Run}");
            var output = Path.Combine(dir, pack.Pack.OutputFile);
            var catalog = pack.Catalog;
            if (r.Trace?.Proposed is { } proposed && File.Exists(Path.Combine(dir, Path.GetFileName(proposed))))
            {
                catalog = catalog.With(Catalog.Domain.PatternParser.Parse(await File.ReadAllTextAsync(Path.Combine(dir, Path.GetFileName(proposed)), ct), Path.GetFileName(proposed)));
            }

            var c = new Cases.Domain.Shared.Case { Id = r.Case, Source = "result", Tags = r.Tags, Request = r.Request, Expect = r.Expect, Hidden = r.Hidden };
            var grades = RunGrading.Grade(c, pack.Pack, catalog, r.Trace, File.Exists(output) ? await File.ReadAllTextAsync(output, ct) : null, gradersToRun);
            var okNow = grades.Pass && r.Agent.Error is null;
            before += r.Pass ? 1 : 0;
            now += okNow ? 1 : 0;
            if (okNow != r.Pass)
            {
                changed.Add($"{r.Case}#{r.Run}: {(r.Pass ? "pass" : "fail")} -> {(okNow ? "pass" : "fail")} {string.Join("; ", grades.Verdicts.Where(v => !v.Value.Ok).Select(v => $"{v.Key}: {v.Value.Failures.FirstOrDefault()}"))}");
            }
        }

        return new RegradeResult(result.RunId, before, now, changed);
    }
}

public sealed record ShowReportCommand(string ResultFile, string? BaselineFile) : ICommand<string>;

public sealed class ShowReportHandler : IHandler<ShowReportCommand, string>
{
    public async Task<Result<string>> HandleAsync(ShowReportCommand command, CancellationToken ct)
    {
        var result = JsonSerializer.Deserialize<EvalResult>(await File.ReadAllTextAsync(command.ResultFile, ct))!;
        var baseline = command.BaselineFile is null ? null : JsonSerializer.Deserialize<EvalResult>(await File.ReadAllTextAsync(command.BaselineFile, ct));
        return Metrics.Report(result, baseline);
    }
}
