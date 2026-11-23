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

    /// <summary>
    /// Runs the claude CLI with these arguments only (no print mode, nothing on standard input) and gives its exit code
    /// and its output. The process tree stops after <paramref name="limit"/>, for example at a prompt that waits for a person.
    /// </summary>
    public static async Task<(int Exit, string Output, string Error)> Plain(string cwd, IReadOnlyList<string> arguments, TimeSpan limit, CancellationToken cancel)
    {
        var info = new ProcessStartInfo(Executable)
        {
            WorkingDirectory = cwd,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"cannot start {Executable}");
        using var tree = new ProcessTree(process, ProcessTree.DefaultInterval);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(limit);
        using var kill = timeout.Token.Register(tree.KillAll);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        await process.WaitForExitAsync(CancellationToken.None);
        if (timeout.IsCancellationRequested && !cancel.IsCancellationRequested) return (-1, await output, $"no end after {limit.TotalSeconds:0} s: {Last(await errors, 400)}");
        return (process.ExitCode, await output, await errors);
    }

    static string Last(string text, int length) => text.Length > length ? text[^length..] : text;

    /// <summary>
    /// The variables of a parent session that a console session must not get: with <c>CLAUDE_CODE_CHILD_SESSION</c> the CLI
    /// saves no transcript, and the others name the parent or its app (R-F12).
    /// </summary>
    public static readonly string[] ParentVariables =
        ["CLAUDE_CODE_CHILD_SESSION", "CLAUDE_CODE_SESSION_ID", "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_HOST_SESSION_ID", "CLAUDE_CODE_MESSAGING_SOCKET", "CLAUDE_CODE_MESSAGING_TOKEN", "CLAUDE_CODE_SESSION_ATTENDED"];

    /// <summary>
    /// Starts the claude CLI as an interactive session in a hidden console and gives its process id. The CLI needs a
    /// console for <c>--remote-control</c>: <c>-p</c> ignores the flag (R-9, R-10). PowerShell <c>Start-Process</c> starts it
    /// through the shell, so the session inherits no handle of this process and stays when this process ends.
    /// </summary>
    public static async Task<int> Console(string cwd, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment, CancellationToken cancel)
    {
        var line = string.Join(' ', arguments.Select(Quote));
        var script = $"$p = Start-Process -FilePath '{Executable.Replace("'", "''")}' -ArgumentList '{line.Replace("'", "''")}' -WorkingDirectory '{cwd.Replace('/', '\\').Replace("'", "''")}' -WindowStyle Hidden -PassThru; $p.Id";
        var info = new ProcessStartInfo("powershell") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script }) info.ArgumentList.Add(argument);
        foreach (var key in ParentVariables) info.Environment.Remove(key);
        foreach (var (key, value) in environment) info.Environment[key] = value;
        using var process = Process.Start(info) ?? throw new InvalidOperationException("cannot start powershell");
        var output = await process.StandardOutput.ReadToEndAsync(cancel);
        var errors = await process.StandardError.ReadToEndAsync(cancel);
        await process.WaitForExitAsync(cancel);
        return int.TryParse(output.Trim().Split('\n').LastOrDefault()?.Trim(), out var pid) && process.ExitCode == 0
            ? pid
            : throw new InvalidOperationException($"the console start failed (exit {process.ExitCode}): {Last(errors.Length > 0 ? errors : output, 400)}");
    }

    /// <summary>One argument in the quoting of a Windows command line: quotes, and backslashes before a quote, doubled as the C runtime reads them.</summary>
    public static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0) return argument;
        var text = new System.Text.StringBuilder("\"");
        var slashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { text.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
            text.Append('\\', slashes).Append(c);
            slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }
}
