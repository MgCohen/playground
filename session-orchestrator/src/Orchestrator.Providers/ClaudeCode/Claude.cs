using System.Diagnostics;
using System.Text.Json;

namespace Orchestrator.Providers.ClaudeCode;

public sealed record ClaudeCall(string Cwd, string Prompt, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment);

public sealed record ClaudeOutput(bool Ok, string? Result, string? Error, IReadOnlyList<JsonElement> Lines);

/// <summary>Runs the claude CLI in print mode and reads its stream-json output.</summary>
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
        using var tree = new ProcessTree(process, ProcessTree.DefaultInterval);
        using var kill = cancel.Register(tree.KillAll);
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
