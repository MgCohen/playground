using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orchestrator.Core.Contract;

namespace Orchestrator.Supervisor;

public sealed record ClaudeCall(string Cwd, string Prompt, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment);

public sealed record ClaudeOutput(bool Ok, string? Result, string? Error, IReadOnlyList<JsonElement> Lines);

public static class Claude
{
    public static string Executable { get; set; } = System.Environment.GetEnvironmentVariable("ORCH_CLAUDE") ?? "claude";

    public static async Task<ClaudeOutput> Run(ClaudeCall call, Action<JsonElement>? onLine, CancellationToken cancel)
    {
        var info = new ProcessStartInfo(Executable)
        {
            WorkingDirectory = call.Cwd,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardInputEncoding = new System.Text.UTF8Encoding(false),
        };
        foreach (var argument in new[] { "-p", "--output-format", "stream-json", "--verbose" }.Concat(call.Arguments)) info.ArgumentList.Add(argument);
        foreach (var (key, value) in call.Environment) info.Environment[key] = value;
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"cannot start {Executable}");
        using var kill = cancel.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        await process.StandardInput.WriteAsync(call.Prompt);
        process.StandardInput.Close();
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var lines = new List<JsonElement>();
        string? result = null;
        string? error = null;
        while (await process.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
        {
            if (!line.StartsWith('{')) continue;
            JsonElement element;
            try
            {
                element = JsonSerializer.Deserialize<JsonElement>(line);
            }
            catch (JsonException)
            {
                continue;
            }
            lines.Add(element);
            onLine?.Invoke(element);
            if (element.TryGetProperty("type", out var type) && type.GetString() == "result")
            {
                var subtype = element.TryGetProperty("subtype", out var s) ? s.GetString() : null;
                var isError = element.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
                if (subtype == "success" && !isError) result = element.TryGetProperty("result", out var r) ? r.GetString() : "";
                else error = subtype ?? "error";
            }
        }
        await process.WaitForExitAsync(CancellationToken.None);
        if (cancel.IsCancellationRequested) return new ClaudeOutput(false, result, "stopped", lines);
        if (result is null && error is null) error = $"exit {process.ExitCode}: {Last(await errors, 400)}";
        return new ClaudeOutput(error is null, result, error, lines);
    }

    static string Last(string text, int length) => text.Length > length ? text[^length..] : text;
}

public sealed class ClaudeRunner(string storeRoot) : ITurnRunner
{
    public async Task<TurnResult> Run(TurnRequest request, CancellationToken cancel)
    {
        var watch = Stopwatch.StartNew();
        var session = request.IsNew ? "--session-id" : "--resume";
        var call = new ClaudeCall(request.Cwd, request.Prompt, ["--setting-sources", "user", session, request.Id],
            new Dictionary<string, string> { ["ORCH_HOLDER"] = Holders.Supervisor, ["ORCH_STORE"] = storeRoot.Replace('\\', '/') });
        var output = await Claude.Run(call, null, cancel);
        return new TurnResult(output.Ok, output.Result, output.Error, Math.Round(watch.Elapsed.TotalSeconds));
    }
}

public sealed partial class ClaudeDocsChecker : IDocsChecker
{
    public async Task<DocsResult> Check(DocsRequest request, CancellationToken cancel)
    {
        var prompt = string.Join('\n',
            "You check if the project documentation already answers a question. Do not change any file.",
            $"Read only these paths in the working folder, if they exist: {string.Join(", ", request.Docs)}.",
            $"Question: {request.Question}",
            $"Options: {(request.Options.Count > 0 ? string.Join(", ", request.Options) : "none given")}",
            "If the documentation gives a clear answer, reply with only this JSON: {\"answered\": true, \"answer\": \"<the answer>\", \"source\": \"<file and section>\"}.",
            "If the answer is not clear in the documentation, reply with only: {\"answered\": false}.");
        var call = new ClaudeCall(request.Cwd, prompt,
            ["--setting-sources", "user", "--no-session-persistence", "--max-turns", "12", "--allowedTools", "Read,Glob,Grep", "--disallowedTools", "Edit,Write,Bash"],
            new Dictionary<string, string>());
        var output = await Claude.Run(call, null, cancel);
        return ParseReply(output.Result ?? "");
    }

    public static DocsResult ParseReply(string text)
    {
        var match = Braces().Match(text);
        if (!match.Success) return new DocsResult(false);
        try
        {
            var value = JsonSerializer.Deserialize<JsonElement>(match.Value);
            return value.TryGetProperty("answered", out var answered) && answered.ValueKind == JsonValueKind.True
                && value.TryGetProperty("answer", out var answer) && answer.ValueKind == JsonValueKind.String
                ? new DocsResult(true, answer.GetString(), value.TryGetProperty("source", out var source) ? source.GetString() : null)
                : new DocsResult(false);
        }
        catch (JsonException)
        {
            return new DocsResult(false);
        }
    }

    [GeneratedRegex(@"\{[\s\S]*\}")]
    private static partial Regex Braces();
}
