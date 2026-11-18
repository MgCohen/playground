using System.Text.Json;

namespace Orchestrator.Core.Contract;

public static class Holders
{
    public const string Live = "live";
    public const string Supervisor = "supervisor";
    public const string None = "none";
}

public sealed record Lease(string Holder, string Since, JsonElement? Reason = null)
{
    /// <summary>The id of the process that holds the lease (the engine process of the session). Null: unknown.</summary>
    public int? Pid { get; init; }

    /// <summary>The start time of that process, so that a reused process id does not count as alive.</summary>
    public string? Started { get; init; }
}

public sealed record Meta(string Id)
{
    public string? Parent { get; init; }
    public string? Title { get; init; }
    public string? Cwd { get; init; }
    public string? CreatedBy { get; init; }
    public string? CreatedAt { get; init; }
    public bool? Started { get; init; }
    public bool? Worktree { get; init; }
    public string? Agent { get; init; }
    public List<string>? Skills { get; init; }
    public string? Repo { get; init; }
    public string? Branch { get; init; }
    public bool? AllowDefaults { get; init; }
    public bool? Imported { get; init; }
    public string? AppId { get; init; }

    /// <summary>True: the brief asked for a background session, so Remote Control stays off.</summary>
    public bool? Background { get; init; }

    /// <summary>True after the session turned Remote Control on for itself; false after it turned it off.</summary>
    public bool? RemoteControl { get; init; }

    /// <summary>Where the session runs: <see cref="Places.Local"/> (null) or <see cref="Places.Cloud"/>.</summary>
    public string? Where { get; init; }
}

/// <summary>Where a session runs. A cloud session runs in a VM of the engine and uses the store API (H5).</summary>
public static class Places
{
    public const string Local = "local";
    public const string Cloud = "cloud";

    public static bool IsCloud(string? where) => where == Cloud;
}

/// <summary>The policy form of a question. The envelope kind is always question.</summary>
public static class DecisionKinds
{
    /// <summary>A question that the policy can answer: it has a risk.</summary>
    public const string AskUser = "ask_user";

    /// <summary>A question with no risk and no default: only a party answers it.</summary>
    public const string Query = "query";

    /// <summary>The policy form of the fields of a question: <see cref="AskUser"/> with a risk or a default, else <see cref="Query"/>.</summary>
    public static string Of(IReadOnlyDictionary<string, object?> fields) =>
        fields.GetValueOrDefault("risk") is null && fields.GetValueOrDefault("defaultOption") is null ? Query : AskUser;
}

public sealed record Decision(string Id, string Session)
{
    /// <summary><see cref="DecisionKinds.AskUser"/> for a question with a risk or a default, else <see cref="DecisionKinds.Query"/>.</summary>
    public string Kind { get; init; } = DecisionKinds.AskUser;
    public string? Question { get; init; }
    public string? Context { get; init; }
    public string? Snippet { get; init; }
    public string? Recommendation { get; init; }
    public string[]? Options { get; init; }
    public JsonElement? Questions { get; init; }
    public string Risk { get; init; } = "medium";
    public string? DefaultOption { get; init; }
    public string Mode { get; init; } = DecisionModes.Pending;
    public double WaitSeconds { get; init; }
    public string Asked { get; init; } = "";
    public string? Event { get; init; }
}

public static class DecisionModes
{
    public const string Wait = "wait";
    public const string Pending = "pending";
}

/// <summary>An answer, read from its envelope. <see cref="Note"/> is the rendered note of a policy or docs answer.</summary>
public sealed record Answer
{
    public string? Value { get; init; }
    public string? By { get; init; }
    public string? Source { get; init; }
    public string? Note { get; init; }
    public string? Reason { get; init; }
    public string? Via { get; init; }
    public string? Event { get; init; }
    public string? At { get; init; }
}

public sealed record Report(string State, string Summary, string At)
{
    public string? Details { get; init; }
    public IReadOnlyList<string>? Done { get; init; }
    public IReadOnlyList<string>? Next { get; init; }
    public IReadOnlyList<string>? Blockers { get; init; }
    public IReadOnlyList<string>? Links { get; init; }
    public string? Event { get; init; }
}

public static class ReportStates
{
    public const string Working = "working";
    public const string Blocked = "blocked";
    public const string Done = "done";
}

public sealed record Review(string Verdict, string Text, string At)
{
    public string? Event { get; init; }
}

public static class Verdicts
{
    public const string Approve = "approve";
    public const string Change = "change";
    public const string Refuse = "refuse";
    public static readonly string[] All = [Approve, Change, Refuse];
}

public sealed record Blocked(string? Reason, string Since)
{
    public string? Event { get; init; }
    /// <summary>supervisor: failed supervisor turns. Absent: the stop latch that the plugin found.</summary>
    public string? By { get; init; }
}

/// <summary>forgotten.json: the nudge of a forgotten session and the notice to its parent.</summary>
public sealed record ForgottenMarker(string Report, string Nudged)
{
    public string? Notified { get; init; }
}

public sealed record Status(string State, string? TurnId, string At);

public sealed record DocsCheck(bool Answered, string? Answer, string? Source, string At)
{
    public string? Error { get; init; }
}

/// <summary>The envelopes to a session that wait for a delivery, and their renders joined into one turn.</summary>
public sealed record Pending(string Text, IReadOnlyList<string> Envelopes)
{
    public IReadOnlyList<string> Senders { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Text.Length == 0;
}

public sealed record TurnMarker(string Event, string From, string At)
{
    public string? Ended { get; init; }
}

public sealed record StoreVersion(int Version, string At)
{
    public const int Current = 2;
}

public sealed record LastOutput(string Event, string At);

public static class ControlStates
{
    public const string Active = "active";
    public const string Stopped = "stopped";
    public const string Archived = "archived";
}

public sealed record Subscription(string Party, string Name, string? Cursor, string At)
{
    public string? Filter { get; init; }
}

public sealed record Control(string State, string At, string By)
{
    public string? Reason { get; init; }
}

/// <summary>
/// The cursor of one subscription, in subscriptions/&lt;party&gt;/&lt;name&gt;.json: the newest done id, and the done ids
/// of the last grace time.
/// </summary>
public sealed record CursorState(string Party, string Name, string? Cursor, List<string> Recent, string At)
{
    public string? Filter { get; init; }
}
