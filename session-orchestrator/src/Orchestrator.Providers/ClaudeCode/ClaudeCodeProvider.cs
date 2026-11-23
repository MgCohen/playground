using System.Diagnostics;
using System.Text.Json;
using Orchestrator.Core.Contract;

namespace Orchestrator.Providers.ClaudeCode;

/// <summary>
/// The Claude Code provider: the claude CLI for turns and model calls, the desktop app for the open and the app facts.
/// The app tools for Remote Control and archive exist only in a live app session, so the notices name them for the model.
/// </summary>
[DefaultProvider]
[Provider("claude-code")]
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
        if (request.RemoteControl) return await ConsoleTurn(request, cancel);
        var watch = Stopwatch.StartNew();
        var session = request.IsNew ? "--session-id" : "--resume";
        List<string> arguments = [.. SettingArguments(options.PluginDir), session, request.Id];
        if (request.IsNew && request.Title is { Length: > 0 } title) arguments.AddRange(["-n", title]);
        if (request.Agent is { Length: > 0 } agent) arguments.AddRange(["--agent", Agent(agent)]);
        var call = new ClaudeCall(request.Cwd, request.Prompt, arguments, TurnEnvironment(options));
        var output = await Claude.Run(call, null, cancel);
        return new TurnResult(output.Ok, output.Result, output.Error, Math.Round(watch.Elapsed.TotalSeconds));
    }

    /// <summary>
    /// The environment of a local turn: the holder, the store and the home. With <see cref="ProviderOptions.OrchDir"/>,
    /// that folder comes first on <c>PATH</c>, so that the session and the plugin call the <c>orch</c> of the supervisor (C3-F5).
    /// </summary>
    public static Dictionary<string, string> TurnEnvironment(ProviderOptions options, string? path = null)
    {
        var environment = new Dictionary<string, string>
        {
            ["ORCH_HOLDER"] = Holders.Supervisor,
            ["ORCH_STORE"] = (options.StoreRoot ?? "").Replace('\\', '/'),
            ["ORCH_HOME"] = (options.HomeRoot ?? "").Replace('\\', '/'),
        };
        if (options.OrchDir is { Length: > 0 } dir)
        {
            path ??= Environment.GetEnvironmentVariable("PATH") ?? "";
            environment["PATH"] = path.Length == 0 ? dir : dir + Path.PathSeparator + path;
        }
        return environment;
    }

    /// <summary>
    /// The arguments of a console start: an interactive session with Remote Control on from the first turn (R-12). It
    /// keeps the session id, so the store entry stays valid. The prompt is short: the brief comes from the plugin.
    /// </summary>
    public static IReadOnlyList<string> ConsoleArguments(TurnRequest request, string? pluginDir)
    {
        var name = request.Title is { Length: > 0 } title ? title : request.Id;
        List<string> arguments = [.. SettingArguments(pluginDir), "--remote-control", name, request.IsNew ? "--session-id" : "--resume", request.Id];
        if (request.IsNew) arguments.AddRange(["-n", name]);
        if (request.Agent is { Length: > 0 } agent) arguments.AddRange(["--agent", Agent(agent)]);
        arguments.Add(request.Prompt);
        return arguments;
    }

    /// <summary>
    /// The environment of a console session: the store, the home, and the holder <c>live</c>. The process is live, so
    /// its plugin takes the lease <c>live</c>, as in an app session.
    /// </summary>
    public static Dictionary<string, string> ConsoleEnvironment(ProviderOptions options)
    {
        var environment = TurnEnvironment(options);
        environment["ORCH_HOLDER"] = Holders.Live;
        return environment;
    }

    async Task<TurnResult> ConsoleTurn(TurnRequest request, CancellationToken cancel)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var pid = await Claude.Console(request.Cwd, ConsoleArguments(request, options.PluginDir), ConsoleEnvironment(options), cancel);
            return new TurnResult(true, $"console session {pid} with Remote Control", null, Math.Round(watch.Elapsed.TotalSeconds)) { Pid = pid };
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return new TurnResult(false, null, e.Message, Math.Round(watch.Elapsed.TotalSeconds));
        }
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

    /// <summary>The desktop app sets this variable in each session that it runs. A headless <c>claude -p</c> turn does not have it (R-16).</summary>
    public const string EntrypointVariable = "CLAUDE_CODE_ENTRYPOINT";

    public const string DesktopEntrypoint = "claude-desktop";

    /// <summary>The app tool that starts a session: in the sidebar at once, with Remote Control from the start (R-16).</summary>
    public const string StartTool = "mcp__ccd_session__start_session";

    static bool SameFolder(string a, string? b) =>
        b is { Length: > 0 } && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public bool CallerInApp() => Environment.GetEnvironmentVariable(EntrypointVariable) == DesktopEntrypoint;

    /// <summary>
    /// The app sets its own id of the session in this variable. A <c>claude -p</c> child of an app session inherits the
    /// id of the parent, so the value counts only in the app (R-18).
    /// </summary>
    public const string HostSessionVariable = "CLAUDE_CODE_HOST_SESSION_ID";

    public string? CallerAppId() =>
        CallerInApp() && Environment.GetEnvironmentVariable(HostSessionVariable) is { } id && id.StartsWith("local_", StringComparison.Ordinal) ? id : null;

    /// <summary>The JSON of the app call keeps quotes and backslashes readable for the model: no ".</summary>
    static readonly JsonSerializerOptions Readable = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// The call of <see cref="StartTool"/> for the parent. The folder is in the form of the system: the app refuses
    /// <c>C:/…</c> on Windows as "another spelling" of <c>C:\…</c> (R-17). The permission mode is explicit: an app session started with
    /// no mode showed <c>auto</c> at the start and <c>default</c> after its first turn (R-16).
    /// </summary>
    public string? AppStartInstruction(AppStartRequest request)
    {
        var prompt = $"You are a child session under the orchestrator. As your first action, run this shell command, and nothing before it: {request.Command} . Then follow the brief that arrives as the next message.";
        var call = new Dictionary<string, object>
        {
            ["initiation"] = "user_asked",
            ["context"] = "fresh",
            ["title"] = request.Title,
            ["use_worktree"] = request.Worktree,
            ["permission_mode"] = "auto",
            ["prompt"] = prompt,
            ["background"] = $"A child session of the orchestrator for the task \"{request.Title}\". The command in the prompt joins it to the orchestrator store; the brief with the goal, the scope and the done check then arrives as a message.",
        };
        // R-25: in a turn that the app continues by itself (a note of a child, an event), the app refuses each explicit
        // folder, also the folder of the parent. A child in the folder of the parent gets none: the app uses that folder.
        if (!SameFolder(request.Cwd, request.ParentCwd)) call["cwd"] = Path.GetFullPath(request.Cwd);
        return $"Start the child in the desktop app now: call {StartTool} with these inputs: {JsonSerializer.Serialize(call, Readable)}. "
            + "Do not change the prompt. If you do not have that tool, or the call is refused (for example \"the user is working over Remote Control\"), do not try again and do not start the child another way: "
            + "call spawn_child again with the same fields and inApp false. Then the supervisor starts the child, opens it in the app and turns its Remote Control on.";
    }

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
