using System.Text.Json;

namespace Orchestrator.Core.Contract;

public static class Holders
{
    public const string Live = "live";
    public const string Supervisor = "supervisor";
    public const string None = "none";
}

public sealed record Lease(string Holder, string Since, JsonElement? Reason = null);

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
}

public sealed record Decision(string Id, string Session)
{
    public string Kind { get; init; } = "ask_user";
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
}

public static class DecisionModes
{
    public const string Wait = "wait";
    public const string Pending = "pending";
}

public sealed record Answer
{
    public string? Text { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("answer")]
    public string? Value { get; init; }
    public JsonElement? Answers { get; init; }
    public string? By { get; init; }
    public string? Source { get; init; }
    public string? Note { get; init; }
    public string? Reason { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Body => Value ?? Text ?? (Answers is { } a ? a.GetRawText() : "");
}

public sealed record Report(string State, string Summary, string At)
{
    public string? Details { get; init; }
    public string? Event { get; init; }
}

public static class ReportStates
{
    public const string Working = "working";
    public const string Blocked = "blocked";
    public const string Done = "done";
}

public sealed record Review(string Verdict, string Text, string At);

public static class Verdicts
{
    public const string Approve = "approve";
    public const string Change = "change";
    public const string Refuse = "refuse";
    public static readonly string[] All = [Approve, Change, Refuse];
}

public sealed record Delivered(string By, string At);

public sealed record Blocked(string? Reason, string Since);

public sealed record Forwarded(string At);

public sealed record Status(string State, string? TurnId, string At);

public sealed record DocsCheck(bool Answered, string? Answer, string? Source, string At)
{
    public string? Error { get; init; }
}

public sealed record Pending(string Text, IReadOnlyList<string> Inbox, IReadOnlyList<string> Answers)
{
    public IReadOnlyList<string> Senders { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Text.Length == 0;
}

public sealed record InboxMessage(string Text, string? From);

public sealed record TurnMarker(string Event, string From, string At)
{
    public string? Ended { get; init; }
}

public sealed record StoreVersion(int Version, string At)
{
    public const int Current = 1;
}

public sealed record LastOutput(string Event, string At);

public static class ControlStates
{
    public const string Active = "active";
    public const string Stopped = "stopped";
    public const string Archived = "archived";
}

public sealed record Control(string State, string At, string By)
{
    public string? Reason { get; init; }
}
