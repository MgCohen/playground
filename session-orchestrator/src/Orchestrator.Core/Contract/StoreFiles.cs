namespace Orchestrator.Core.Contract;

public static class StoreFiles
{
    public const string Sessions = "sessions";
    public const string Enabled = "enabled";
    public const string Lease = "lease.json";
    public const string Heartbeat = "heartbeat.txt";
    public const string Status = "status.json";
    public const string Inbox = "inbox";
    public const string Cursor = "cursor.txt";
    public const string Mirror = "mirror";
    public const string Decisions = "decisions";
    public const string Answers = "answers";
    public const string Delivered = "delivered";
    public const string Report = "report.json";
    public const string Reports = "reports";
    public const string Meta = "meta.json";
    public const string Blocked = "blocked.json";
    public const string DocsChecks = "docs-checks";
    public const string Reviews = "reviews";
    public const string Forwarded = "forwarded.json";
    public const string Policy = "policy.json";
    public const string Ledger = "ledger.jsonl";
    public const string SupervisorLog = "supervisor/log.txt";
    public const string Events = "events";
    public const string Bodies = "bodies";
    public const string Stack = "stack";
    public const string Turn = "turn.json";
    public const string LastOutput = "last-output.json";
    public const string Control = "control.json";
    public const string Update = "update.json";

    public static string Json(string id) => $"{id}.json";
}
