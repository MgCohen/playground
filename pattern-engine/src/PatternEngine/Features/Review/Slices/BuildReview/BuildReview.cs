using System.Net;
using System.Text;
using System.Text.Json;
using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Eval.Domain;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Features.Trace.Domain.Shared;
using PatternEngine.Plugins.Messages;

namespace PatternEngine.Features.Review.Slices.BuildReview;

public sealed record BuildReviewCommand(IReadOnlyList<string> ResultFiles, string? Title) : ICommand<string>;

/// <summary>
/// Builds the review page from result files: one card for each run, with the request, the engine verdict, the output and the
/// expected answer. The pack renders the output and the expected answer; the core renders the trace.
/// </summary>
public sealed class BuildReviewHandler(PackRegistry packs) : IHandler<BuildReviewCommand, string>
{
    public async Task<Result<string>> HandleAsync(BuildReviewCommand command, CancellationToken ct)
    {
        var items = new List<object>();
        var runs = new List<object>();
        string? reviewDir = null;
        string? template = null;
        foreach (var file in command.ResultFiles)
        {
            var result = JsonSerializer.Deserialize<EvalResult>(await File.ReadAllTextAsync(file, ct))!;
            var pack = packs.Load(result.Domain);
            var paths = pack.Settings.Section("paths");
            reviewDir ??= packs.Root.Path(paths.Text("review"));
            template ??= await File.ReadAllTextAsync(packs.Root.Path(paths.Text("review_template")), ct);
            var runsDir = Path.Combine(packs.Root.Path(paths.Text("runs")), result.RunId);
            runs.Add(new { runId = result.RunId, harness = $"{result.Engine} · {result.Domain}", model = result.Model, k = result.K, mode = result.Mode, runs = result.Runs.Count });
            foreach (var r in result.Runs)
            {
                var output = Path.Combine(runsDir, $"{r.Case}-{r.Run}", pack.Pack.OutputFile);
                var c = new Case { Id = r.Case, Source = "result", Tags = r.Tags, Request = r.Request, Expect = r.Expect, Hidden = r.Hidden };
                var expected = pack.Pack.Review?.RenderExpected(c, pack.Catalog) ?? $"<pre>{Encode(r.Expect.Tree?.ToString() ?? r.Expect.Outcome)}</pre>";
                items.Add(new
                {
                    key = $"{result.RunId}__{r.Case}-{r.Run}".Replace(':', '_').Replace('.', '_'),
                    runId = result.RunId,
                    harness = $"{result.Engine} · {result.Domain}",
                    model = result.Model,
                    @case = r.Case,
                    run = r.Run,
                    tags = r.Tags,
                    request = r.Request,
                    output = File.Exists(output)
                        ? pack.Pack.Review?.RenderOutput(await File.ReadAllTextAsync(output, ct)) ?? $"<pre>{Encode(await File.ReadAllTextAsync(output, ct))}</pre>"
                        : "<p class=\"none\">No output file.</p>",
                    expected,
                    expectedVisual = expected,
                    trace = Trace(r.Trace),
                    engine = new
                    {
                        pass = r.Pass,
                        graders = r.Grades.Verdicts.Where(v => !v.Value.Skipped).Select(v => new { name = v.Key, ok = v.Value.Ok, why = v.Value.Ok ? string.Empty : v.Value.Failures.FirstOrDefault() ?? string.Empty }),
                        traceMatches = r.Grades.Consistent,
                    },
                    agent = new { secs = r.Agent.Ms / 1000, turns = r.Agent.Calls, subagents = r.Agent.Escalations },
                });
            }
        }

        var title = command.Title ?? "Eval review";
        var data = JsonSerializer.Serialize(new { title, built = DateTime.UtcNow.ToString("O"), runs, items }).Replace("<", "\\u003c", StringComparison.Ordinal);
        var html = template!.Replace("/*__DATA__*/null", data, StringComparison.Ordinal).Replace("{{TITLE}}", Encode(title), StringComparison.Ordinal);
        Directory.CreateDirectory(reviewDir!);
        var outFile = Path.Combine(reviewDir!, $"review-{Path.GetFileNameWithoutExtension(command.ResultFiles[0])}.html");
        await File.WriteAllTextAsync(outFile, html, ct);
        return outFile;
    }

    private static string Encode(string s) => WebUtility.HtmlEncode(s);

    /// <summary>The trace as HTML: outcome, tree, questions, notes and the stop reason.</summary>
    private static string Trace(RunTrace? t)
    {
        if (t is null)
        {
            return "<p class=\"none\">No trace.</p>";
        }

        var sb = new StringBuilder($"<p>Outcome: <b>{Encode(t.Outcome)}</b>{(t.Nearest is null ? string.Empty : $" · nearest: <b>{Encode(t.Nearest)}</b>")}{(t.StopReason is null ? string.Empty : $" · stop: {Encode(t.StopReason)}")}</p>");
        if (t.Tree is not null)
        {
            sb.Append($"<ol class=\"tree\">{Tree(t.Tree)}</ol>");
        }

        var lines = t.Questions.Select(q => $"Question ({q.About}, {q.Key}): {q.Text} → {q.Answer ?? "no answer"}").Concat(t.Notes).Concat(t.OpenRequirements.Select(o => $"Open requirement: {o}")).ToList();
        if (lines.Count > 0)
        {
            sb.Append($"<ul class=\"qs\">{string.Concat(lines.Select(l => $"<li>{Encode(l)}</li>"))}</ul>");
        }

        return sb.ToString();
    }

    private static string Tree(TreeNode t) =>
        $"<li><span class=\"pat\">{Encode(t.P)}</span>{(t.V is null ? string.Empty : $"<span class=\"var\">{Encode(t.V)}</span>")}"
        + string.Concat((t.Params ?? []).Select(p => $"<span class=\"param\">{Encode(p.Key.Replace('_', ' '))}: {Encode(p.Value)}</span>"))
        + (t.Parts is { Count: > 0 } ? $"<ol>{string.Concat(t.Parts.Select(Tree))}</ol>" : string.Empty) + "</li>";
}
