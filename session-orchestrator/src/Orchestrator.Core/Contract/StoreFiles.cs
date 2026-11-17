namespace Orchestrator.Core.Contract;

public static class StoreFiles
{
    public const string Sessions = "sessions";
    public const string Enabled = "enabled";
    public const string Lease = "lease.json";
    public const string Heartbeat = "heartbeat.txt";
    public const string Status = "status.json";
    public const string Mirror = "mirror";
    public const string Meta = "meta.json";
    public const string DocsChecks = "docs-checks";
    public const string Policy = "policy.json";
    public const string SupervisorLog = "supervisor/log.txt";
    public const string Events = "events";
    public const string Bodies = "bodies";
    public const string Subscriptions = "subscriptions";
    public const string Nudges = "nudges";
    public const string Claims = "claims";
    public const string Stack = "stack";
    public const string Turn = "turn.json";
    public const string LastOutput = "last-output.json";
    public const string Control = "control.json";
    public const string Version = "version.json";
    public const string State = "state.json";
    public const string StateCheck = "state-check.json";
    public const string Forgotten = "forgotten.json";
    public const string Templates = "templates";

    public static string Json(string id) => $"{id}.json";
}
