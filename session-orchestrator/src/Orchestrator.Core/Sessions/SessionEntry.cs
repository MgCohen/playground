using Orchestrator.Core.Contract;

namespace Orchestrator.Core.Sessions;

/// <summary>
/// One entry of the session registry: <c>sessions/&lt;id&gt;/session.json</c> in the local folder store (store version 3).
/// Only <see cref="SessionRegistry"/> writes it. The run state (lease, heartbeat, status, turn, state machine) stays in
/// its own files. The children of a session are a query (<see cref="SessionRegistry.Children"/>), not a field.
/// </summary>
public sealed record SessionEntry(string Id)
{
    /// <summary>The own code, unique among the siblings, for example <c>C2</c>. The full code is a query of the parent chain.</summary>
    public string? Code { get; init; }

    /// <summary>References to the engine of the session. Only <c>IProvider</c> reads them.</summary>
    public EngineRef Engine { get; init; } = new();

    /// <summary>The parent session id. Null for a root.</summary>
    public string? Parent { get; init; }

    public string? Title { get; init; }

    /// <summary>The party that owns the work, for example <c>user</c>.</summary>
    public string? Owner { get; init; }

    public List<string>? Tags { get; init; }

    /// <summary>The stream id of C1, for example <c>S</c>. Null: no stream.</summary>
    public string? Stream { get; init; }

    /// <summary>The relation of the session to workflows: <see cref="Roles"/>.</summary>
    public string Role { get; init; } = Roles.Free;

    /// <summary>The optional custom agent of the session. Only the provider reads it to start the session.</summary>
    public SessionType? Type { get; init; }

    /// <summary>The workflow that the session runs or works in. Null: none.</summary>
    public string? Workflow { get; init; }

    /// <summary>The folder, and the git worktree when the brief asked for one.</summary>
    public SessionWork Work { get; init; } = new();

    /// <summary>The settings in tiers. Each entry holds a copy of each value that it got at create.</summary>
    public SessionSettings Settings { get; init; } = new();

    /// <summary>The phase (active, stopped, archived) and the start mark. The state machine stays in <c>state.json</c>.</summary>
    public SessionLifecycle Lifecycle { get; init; } = new();

    public Stamp? Created { get; init; }

    public Stamp? Updated { get; init; }

    /// <summary>
    /// True for visibility private (tier behavior): today it only keeps Remote Control off, as <c>background</c> did in
    /// store version 2. S4 adds the other effects of the axis.
    /// </summary>
    public bool IsPrivate() => Settings.Text(SettingTiers.Behavior, SettingTiers.Visibility) == SettingTiers.Private;

    /// <summary>True while the next turn must make the engine session: the registry made the entry, and no turn started it.</summary>
    public bool IsNew() => Created is not null && Lifecycle.Started != true;
}

/// <summary>
/// The references of a session to its engine: the kind of the provider, the engine session id and the app id. The two
/// flags record what our system did or was told: <see cref="Imported"/> after a confirmed import, and
/// <see cref="RemoteControl"/> after a session reported a remote control change. No other engine fact is kept.
/// </summary>
public sealed record EngineRef
{
    /// <summary>The name of the provider (<c>IProvider.Name</c>).</summary>
    public string? Kind { get; init; }

    /// <summary>The session id in the engine. Today it is the registry id.</summary>
    public string? SessionId { get; init; }

    /// <summary>The id of the session in the app of the engine.</summary>
    public string? AppId { get; init; }

    /// <summary>True after the app confirmed an import.</summary>
    public bool? Imported { get; init; }

    /// <summary>True or false after a remote control call on the session, by the session itself or by its parent.</summary>
    public bool? RemoteControl { get; init; }

    /// <summary>
    /// True after the supervisor started the session as a live process in a console with Remote Control. The archive
    /// stops that process: no app owns it.
    /// </summary>
    public bool? Console { get; init; }
}

public sealed record SessionWork
{
    public string? Cwd { get; init; }
    public string? Repo { get; init; }
    public string? Branch { get; init; }

    /// <summary>True: the first turn makes a git worktree. Then <see cref="Cwd"/> is the worktree folder.</summary>
    public bool? Worktree { get; init; }

    /// <summary>Where the session runs (G2): <see cref="Places.Local"/> (null) or <see cref="Places.Cloud"/>.</summary>
    public string? Where { get; init; }
}

/// <summary>A custom agent: the agent name and the skills to load first.</summary>
public sealed record SessionType
{
    public string? Agent { get; init; }
    public List<string>? Skills { get; init; }
}

public sealed record SessionLifecycle
{
    /// <summary><see cref="ControlStates"/>: active, stopped or archived.</summary>
    public string Phase { get; init; } = ControlStates.Active;

    /// <summary>The time of the last phase change.</summary>
    public string? At { get; init; }

    /// <summary>The party of the last phase change.</summary>
    public string? By { get; init; }

    public string? Reason { get; init; }

    /// <summary>True after the first turn started the engine session. Not true: the next turn makes a new engine session.</summary>
    public bool? Started { get; init; }
}

/// <summary>A time and a party.</summary>
public sealed record Stamp(string At, string By);

public static class Roles
{
    public const string Orchestrator = "orchestrator";
    public const string Worker = "worker";
    public const string Free = "free";

    public static readonly IReadOnlyList<string> All = [Orchestrator, Worker, Free];

    /// <summary>
    /// The default agent of a role (C1): the plugin agents orchestrator and worker. A free session has none. A type
    /// (<see cref="SessionEntry.Type"/>) replaces the default agent. The provider maps the name to its engine.
    /// </summary>
    public static string? DefaultAgent(string? role) => role switch
    {
        Orchestrator => Orchestrator,
        Worker => Worker,
        _ => null,
    };

    /// <summary>The agent of a session: its type, else the default agent of its role.</summary>
    public static string? AgentOf(SessionEntry entry) => entry.Type?.Agent is { Length: > 0 } agent ? agent : DefaultAgent(entry.Role);

    /// <summary>
    /// The role of a new session before a workflow run sets it (C1): orchestrator for the orchestrator agent, worker for a
    /// child, else free.
    /// </summary>
    public static string Initial(string? agent, string? parent) =>
        agent is not null && agent.Split(':')[^1] == Orchestrator ? Orchestrator
        : parent is not null ? Worker
        : Free;
}
