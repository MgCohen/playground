using System.Diagnostics;
using System.Text.Json;
using Orchestrator.Core.Contract;

namespace Orchestrator.Providers.ClaudeCode;

/// <summary>
/// The Claude Code provider: the claude CLI for turns and model calls, the desktop app for the open and the app facts.
/// The app tools for Remote Control and archive exist only in a live app session, so the notices name them for the model.
/// </summary>
[DefaultProvider]
public sealed partial class ClaudeCodeProvider(ProviderOptions options) : IProvider
{
    /// <summary>The namespace of the orch plugin in Claude Code: an agent name without a namespace gets it.</summary>
    public const string PluginNamespace = "orch";

    public const string SessionIdVariable = "CLAUDE_CODE_SESSION_ID";

    public string Name => "Claude Code";

    public string PluginTools => "mcp__orch__";

    public IReadOnlyList<(string Key, object? Value)> AppTools { get; } =
        [("remoteControlTool", "mcp__ccd_session_mgmt__set_remote_control"), ("archiveTool", "mcp__ccd_session_mgmt__archive_session")];

    public static IReadOnlyList<string> SettingArguments(string? pluginDir) =>
        pluginDir is null ? ["--setting-sources", "user"] : ["--setting-sources", "project,local", "--permission-mode", "auto", "--plugin-dir", pluginDir];

    static string Agent(string agent) => agent.Contains(':') ? agent : $"{PluginNamespace}:{agent}";

    public async Task<TurnResult> Turn(TurnRequest request, CancellationToken cancel)
    {
        if (request.Where == Places.Cloud) return await CloudTurn(request, cancel);
        var watch = Stopwatch.StartNew();
        var session = request.IsNew ? "--session-id" : "--resume";
        List<string> arguments = [.. SettingArguments(options.PluginDir), session, request.Id];
        if (request.IsNew && request.Title is { Length: > 0 } title) arguments.AddRange(["-n", title]);
        if (request.Agent is { Length: > 0 } agent) arguments.AddRange(["--agent", Agent(agent)]);
        var call = new ClaudeCall(request.Cwd, request.Prompt, arguments,
            new Dictionary<string, string>
            {
                ["ORCH_HOLDER"] = Holders.Supervisor,
                ["ORCH_STORE"] = (options.StoreRoot ?? "").Replace('\\', '/'),
                ["ORCH_HOME"] = (options.HomeRoot ?? "").Replace('\\', '/'),
            });
        var output = await Claude.Run(call, null, cancel);
        return new TurnResult(output.Ok, output.Result, output.Error, Math.Round(watch.Elapsed.TotalSeconds));
    }

    /// <summary>The longest wait for a cloud start or a cloud message. A folder that is not trusted yet waits for a person.</summary>
    public static TimeSpan CloudLimit { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The arguments of a cloud turn. A start is <c>claude --cloud "&lt;prompt&gt;"</c>: it prints the session id and the
    /// view URL, then exits (print mode refuses <c>--cloud</c>). A later turn is <c>claude -p "&lt;prompt&gt;" --cloud &lt;id&gt;</c>:
    /// it sends one message. The cloud session gets the plugin, the permissions and <c>ORCH_URL</c> from its cloud
    /// environment (G-4), not from this process. The end-to-end test with a real cloud session comes with D2.
    /// </summary>
    public static IReadOnlyList<string> CloudArguments(TurnRequest request) =>
        request.IsNew || request.AppId is null ? ["--cloud", request.Prompt] : ["-p", request.Prompt, "--cloud", request.AppId];

    /// <summary>The cloud session id in the output of a cloud start, for example <c>session_01Y7UMGXQGcsekzz7bDemKjq</c>.</summary>
    public static string? CloudSessionId(string output) => CloudId().Match(output) is { Success: true } m ? m.Value : null;

    [System.Text.RegularExpressions.GeneratedRegex(@"\bsession_[0-9A-Za-z]{8,}\b")]
    private static partial System.Text.RegularExpressions.Regex CloudId();

    /// <summary>
    /// A cloud turn ends when the engine took the prompt, not when the cloud session ends its turn. The cloud session
    /// reports through the store API (H5).
    /// </summary>
    async Task<TurnResult> CloudTurn(TurnRequest request, CancellationToken cancel)
    {
        var watch = Stopwatch.StartNew();
        if (!request.IsNew && request.AppId is null) return new TurnResult(false, null, "the cloud session has no id", 0);
        var (exit, output, error) = await Claude.Plain(request.Cwd, CloudArguments(request), CloudLimit, cancel);
        var seconds = Math.Round(watch.Elapsed.TotalSeconds);
        if (exit != 0) return new TurnResult(false, null, $"cloud exit {exit}: {Cut(error.Length > 0 ? error : output)}", seconds);
        var appId = request.IsNew ? CloudSessionId(output) : request.AppId;
        return new TurnResult(true, Cut(output), null, seconds) { AppId = appId };
    }

    static string Cut(string text) => text.Trim() is { Length: > 400 } long_ ? long_[^400..] : text.Trim();

    public async Task<AskResult> Ask(AskRequest request, CancellationToken cancel)
    {
        List<string> arguments = [.. SettingArguments(request.Plugin ? options.PluginDir : null), "--no-session-persistence", "--max-turns", request.MaxTurns.ToString()];
        if (request.Agent is { Length: > 0 } agent) arguments.AddRange(["--agent", Agent(agent)]);
        arguments.AddRange(["--allowedTools", "Read,Glob,Grep", "--disallowedTools", "Edit,Write,Bash,PowerShell"]);
        var output = await Claude.Run(new ClaudeCall(request.Cwd, request.Prompt, arguments, new Dictionary<string, string>()), null, cancel);
        return new AskResult(output.Ok, output.Result, output.Error);
    }

    public IReadOnlyList<string> OpenCommand(string sessionId, string? cwd = null)
    {
        var folder = cwd is { Length: > 0 } ? $" -WorkingDirectory '{cwd.Replace('/', '\\')}'" : "";
        return ["powershell", "-NoProfile", "-Command", $"Start-Process -FilePath 'claude.exe' -ArgumentList '--desktop','--resume','{sessionId}'{folder} -WindowStyle Hidden"];
    }

    public string AppId(string sessionId) => $"local_{sessionId}";

    public AppFacts Read(string appId)
    {
        var inApp = AppRecords.Exists(appId);
        return new AppFacts(inApp, inApp && AppRecords.IsArchived(appId));
    }

    public string? CallerSessionId() => Environment.GetEnvironmentVariable(SessionIdVariable) is { Length: > 0 } id ? id : null;

    public IReadOnlyList<DoctorCheck> Doctor(CommandRun run) =>
    [
        new("claude", () => run("claude", "--version") is var r && r.Ok ? (true, r.Text) : (false, "not on the PATH")),
        new("plugin", () =>
        {
            var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");
            var json = JsonDocument.Parse(File.ReadAllText(settings)).RootElement;
            if (!json.TryGetProperty("env", out var env) || !env.TryGetProperty("CLAUDE_CODE_PLUGIN_DIRS", out var dirs)) return (false, "CLAUDE_CODE_PLUGIN_DIRS is not in the env block of ~/.claude/settings.json");
            var path = dirs.GetString() ?? "";
            return (File.Exists(Path.Combine(path, ".claude-plugin", "plugin.json")), path);
        }),
    ];
}
