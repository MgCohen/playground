namespace Orchestrator.Providers;

/// <summary>
/// A session engine. Each function that depends on an engine goes through this interface: a model call, a session turn,
/// an app fact, a CLI command, a setup check. Only the folder of a provider knows the names of its engine.
/// </summary>
public interface IProvider
{
    /// <summary>The name of the engine in texts for people.</summary>
    string Name { get; }

    /// <summary>One turn of a session: the first turn of a new session, or the next turn of a known session.</summary>
    Task<TurnResult> Turn(TurnRequest request, CancellationToken cancel);

    /// <summary>One model call with no session and read-only tools. The caller writes the prompt and parses the reply.</summary>
    Task<AskResult> Ask(AskRequest request, CancellationToken cancel);

    /// <summary>The command that opens a session in the app. The first item is the program.</summary>
    IReadOnlyList<string> OpenCommand(string sessionId, string? cwd = null);

    /// <summary>The id that the app gives to a session after the open.</summary>
    string AppId(string sessionId);

    /// <summary>The app facts of one app id, read on demand. No model call.</summary>
    AppFacts Read(string appId);

    /// <summary>The engine session id of the session that runs this process. Null: the process does not run in a session.</summary>
    string? CallerSessionId();

    /// <summary>
    /// True when the process that runs this call is a session in the app of the engine, with the app tools. A parent in
    /// the app starts its children with the app tool, so they show in the app with Remote Control from the start (R-F8).
    /// </summary>
    bool CallerInApp();

    /// <summary>
    /// The instruction for a parent in the app: start the child with the app tool, with this first command. Null: the
    /// engine cannot start a session from the app, and the supervisor starts the child.
    /// </summary>
    string? AppStartInstruction(AppStartRequest request);

    /// <summary>The notice fields that name the app tools for Remote Control and archive.</summary>
    IReadOnlyList<(string Key, object? Value)> AppTools { get; }

    /// <summary>The text that names the tools of the orch plugin in this engine.</summary>
    string PluginTools { get; }

    /// <summary>The setup checks of the engine for orch doctor.</summary>
    IReadOnlyList<DoctorCheck> Doctor(CommandRun run);
}

/// <summary>Runs a program and gives its result. The caller decides the working folder.</summary>
public delegate (bool Ok, string Text) CommandRun(string program, params string[] arguments);

/// <summary>One setup check of orch doctor.</summary>
public sealed record DoctorCheck(string Name, Func<(bool Ok, string Detail)> Test);

/// <summary>A child that a parent in the app starts. <c>Command</c>: the first command of the child, which joins it to the store.</summary>
public sealed record AppStartRequest(string Title, string Cwd, bool Worktree, string Command);

/// <summary>The app facts of one session. <c>InApp</c>: the app has a record of the session.</summary>
public sealed record AppFacts(bool InApp, bool Archived);

public sealed record TurnRequest(string Id, string Cwd, string Prompt, bool IsNew)
{
    public string? Agent { get; init; }
    public string? Title { get; init; }

    /// <summary>Where the session runs: null or <c>local</c> on this computer, <c>cloud</c> in a VM of the engine.</summary>
    public string? Where { get; init; }

    /// <summary>The id that the engine gave to the session at its start, for a session that the engine names (cloud).</summary>
    public string? AppId { get; init; }
}

/// <summary>The result of a turn. <c>AppId</c>: the id that the engine gave to a new session that it names (cloud).</summary>
public sealed record TurnResult(bool Ok, string? Text, string? Error, double Seconds)
{
    public string? AppId { get; init; }
}

/// <summary>A one-shot model call. <c>Agent</c>: an agent of the orch plugin. <c>Plugin</c>: load the orch plugin as the turns do.</summary>
public sealed record AskRequest(string Cwd, string Prompt, int MaxTurns)
{
    public string? Agent { get; init; }
    public bool Plugin { get; init; }
}

public sealed record AskResult(bool Ok, string? Text, string? Error);

/// <summary>The values of the composition root. <c>PluginDir</c>: load the orch plugin from this folder. Null: from the user settings.</summary>
public sealed record ProviderOptions(string? StoreRoot = null, string? HomeRoot = null, string? PluginDir = null)
{
    /// <summary>The folder of the <c>orch</c> tool of the supervisor (C3-F5). Each turn gets it first on <c>PATH</c>. Null: the <c>PATH</c> does not change.</summary>
    public string? OrchDir { get; init; }
}
