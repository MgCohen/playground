using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchestrator.Providers.Fake;

/// <summary>
/// A provider with no model and no app: each call gives the result of a behavior file. Demos and tests use it to run
/// the supervisor, the tray and the UI on dummy data with no model call (D, step 3). <c>ORCH_PROVIDER=fake</c> selects
/// it. <c>ORCH_FAKE</c> names the behavior file. The provider reads the file at each call, so a test can change the
/// behavior while the supervisor runs. With no file, each turn is good and each model call has no answer.
/// </summary>
[Provider(ProviderName)]
public sealed class FakeProvider(ProviderOptions options) : IProvider
{
    public const string ProviderName = "fake";
    public const string BehaviorVariable = "ORCH_FAKE";

    /// <summary>The behavior file. A path in the options comes first (tests), then <c>ORCH_FAKE</c>.</summary>
    public string? BehaviorFile { get; init; } = Environment.GetEnvironmentVariable(BehaviorVariable) is { Length: > 0 } path ? path : null;

    /// <summary>The turns that this provider ran, in order. A test reads them.</summary>
    public List<TurnRequest> Turns { get; } = [];

    public ProviderOptions Options => options;

    public string Name => "Fake";

    public string PluginTools => "mcp__orch__";

    public IReadOnlyList<(string Key, object? Value)> AppTools { get; } = [("remoteControlTool", "fake_remote_control"), ("archiveTool", "fake_archive")];

    public IReadOnlyList<(string Key, object? Value)> GroupTools { get; } = [("groupTool", "fake_move_sessions"), ("groupCreateTool", "fake_create_group"), ("group", "Workers")];

    public string? WorkerGroupInstruction(string appId) => $"fake_move_sessions session={appId} group=Workers";

    public async Task<TurnResult> Turn(TurnRequest request, CancellationToken cancel)
    {
        lock (Turns) Turns.Add(request);
        var rule = Load().TurnFor(request.Id);
        if (rule.DelayMs is > 0 and var delay) await Task.Delay(delay, cancel);
        return rule.Ok
            ? new TurnResult(true, rule.Text ?? $"fake turn of {request.Id}", null, 0) { AppId = request.Where is null ? null : AppId(request.Id) }
            : new TurnResult(false, null, rule.Error ?? "fake turn failed", 0);
    }

    public Task<AskResult> Ask(AskRequest request, CancellationToken cancel)
    {
        var ask = Load().Ask;
        return Task.FromResult(ask is { Ok: true } ? new AskResult(true, ask.Text ?? "", null) : new AskResult(false, null, ask?.Error ?? "the fake provider has no answer"));
    }

    public IReadOnlyList<string> OpenCommand(string sessionId, string? cwd = null) => ["fake-open", sessionId];

    public string AppId(string sessionId) => $"fake_{sessionId}";

    public AppFacts Read(string appId)
    {
        var app = Load().App;
        return new AppFacts(app?.InApp ?? false, app?.Archived ?? false);
    }

    public string? CallerSessionId() => null;

    /// <summary>True when the behavior file has <c>"app": { "caller": true }</c>.</summary>
    public bool CallerInApp() => Load().App?.Caller ?? false;

    public string? CallerAppId() => null;

    public string? AppStartInstruction(AppStartRequest request) => $"fake_start_session title={request.Title} cwd={request.Cwd} worktree={request.Worktree} command={request.Command}";

    public IReadOnlyList<DoctorCheck> Doctor(CommandRun run) =>
        [new DoctorCheck("fake provider", () => (true, BehaviorFile is null ? "no behavior file: each turn is good" : $"behavior file {BehaviorFile}"))];

    Behavior Load()
    {
        if (BehaviorFile is not { } file || !File.Exists(file)) return new Behavior();
        try
        {
            return JsonSerializer.Deserialize<Behavior>(File.ReadAllText(file), Json) ?? new Behavior();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return new Behavior { Turn = new TurnRule { Ok = false, Error = $"the behavior file {file} is not valid: {e.Message}" } };
        }
    }

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>
    /// The behavior file: <c>{ "turn": rule, "sessions": { "&lt;id or prefix&gt;": rule }, "ask": { ok, text, error }, "app": { inApp, archived } }</c>.
    /// A rule is <c>{ ok, text, error, delayMs }</c>. The longest matching prefix of <c>sessions</c> wins, else <c>turn</c>.
    /// </summary>
    public sealed record Behavior
    {
        public TurnRule Turn { get; init; } = new();
        public Dictionary<string, TurnRule> Sessions { get; init; } = [];
        public AskRule? Ask { get; init; }
        public AppRule? App { get; init; }

        public TurnRule TurnFor(string id) =>
            Sessions.Where(s => id.StartsWith(s.Key, StringComparison.OrdinalIgnoreCase)).OrderByDescending(s => s.Key.Length).Select(s => s.Value).FirstOrDefault() ?? Turn;
    }

    public sealed record TurnRule
    {
        public bool Ok { get; init; } = true;
        public string? Text { get; init; }
        public string? Error { get; init; }
        public int? DelayMs { get; init; }
    }

    public sealed record AskRule
    {
        public bool Ok { get; init; } = true;
        public string? Text { get; init; }
        public string? Error { get; init; }
    }

    public sealed record AppRule
    {
        public bool InApp { get; init; }
        public bool Archived { get; init; }

        /// <summary>The process that calls the provider runs in the app (the caller of <c>orch new --app-start</c>).</summary>
        public bool Caller { get; init; }
    }
}
