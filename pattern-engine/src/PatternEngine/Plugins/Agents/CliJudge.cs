using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PatternEngine.Plugins.Agents;

/// <summary>
/// Runs one agent call with the Claude CLI and the subscription login of the user:
/// claude -p --agent NAME --json-schema SCHEMA, with the input on stdin.
/// Never --bare: bare mode does not read the subscription login.
/// The agent file is copied into WORKDIR/.claude/agents so that --agent finds it.
/// </summary>
public sealed class CliJudge(AgentCatalog agents, CliJudgeSettings settings) : IJudge
{
    public async Task<AgentAnswer> CallAsync(AgentCall call, CancellationToken ct)
    {
        var agent = agents.Get(call.Agent);
        Install(agent, call.WorkDir);
        AgentAnswer? last = null;
        long ms = 0;
        double cost = 0;
        for (var i = 0; i <= settings.Retries; i++)
        {
            last = await OnceAsync(agent, call, ct);
            ms += last.Ms;
            cost += last.Cost;
            if (last.Ok)
            {
                break;
            }
        }

        return last! with { Ms = ms, Cost = cost };
    }

    public string Version(string agent)
    {
        var file = agents.Get(agent);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(file.Text)))[..12];
        return $"{hash}/{settings.Model ?? file.Model}";
    }

    private static void Install(AgentFile agent, string workDir)
    {
        var dir = Path.Combine(workDir, ".claude", "agents");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, $"{agent.Name}.md");
        if (!File.Exists(target))
        {
            File.WriteAllText(target, agent.Text);
        }
    }

    private async Task<AgentAnswer> OnceAsync(AgentFile agent, AgentCall call, CancellationToken ct)
    {
        var start = Stopwatch.StartNew();
        var info = new ProcessStartInfo(settings.Executable)
        {
            WorkingDirectory = call.WorkDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        string[] args =
        [
            "-p", "--agent", agent.Name, "--json-schema", agent.Schema.GetRawText(), "--output-format", "json",
            "--setting-sources", "project", "--strict-mcp-config", "--no-session-persistence",
            "--model", settings.Model ?? agent.Model,
        ];
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("cannot start the claude CLI");
        await process.StandardInput.WriteAsync(call.Input);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            return new AgentAnswer(null, start.ElapsedMilliseconds, 0, "timeout");
        }

        var text = await output;
        var stderr = await error;
        var brace = text.IndexOf('{');
        if (brace < 0)
        {
            return new AgentAnswer(null, start.ElapsedMilliseconds, 0, Tail($"exit {process.ExitCode}: {stderr} {text}"));
        }

        try
        {
            using var doc = JsonDocument.Parse(text[brace..]);
            var root = doc.RootElement;
            var cost = root.TryGetProperty("total_cost_usd", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : 0;
            if (root.TryGetProperty("structured_output", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                return new AgentAnswer(data.Clone(), start.ElapsedMilliseconds, cost, null);
            }

            var result = root.TryGetProperty("result", out var r) ? r.ToString() : string.Empty;
            return new AgentAnswer(null, start.ElapsedMilliseconds, cost, Tail($"no structured output: {result} {stderr}"));
        }
        catch (JsonException)
        {
            return new AgentAnswer(null, start.ElapsedMilliseconds, 0, Tail($"exit {process.ExitCode}: {stderr} {text}"));
        }
    }

    private static string Tail(string text) => text.Length <= 300 ? text.Trim() : text[^300..].Trim();
}

public sealed record CliJudgeSettings(string Executable, string? Model, int Retries, int TimeoutSeconds);
