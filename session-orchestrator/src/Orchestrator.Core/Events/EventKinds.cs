namespace Orchestrator.Core.Events;

public static class EventKinds
{
    public const string Decision = "decision";
    public const string Answer = "answer";
    public const string Review = "review";
    public const string Report = "report";
    public const string Update = "update";
    public const string Brief = "brief";
    public const string Notice = "notice";
    public const string Message = "message";

    public const string ReportForward = "report.forward";
    public const string SessionInput = "session.input";
    public const string SessionOutput = "session.output";
    public const string SessionTool = "session.tool";
    public const string SessionStart = "session.start";
    public const string SessionEnd = "session.end";
    public const string SessionStop = "session.stop";
    public const string SessionArchive = "session.archive";

    public const string TurnStart = "turn.start";
    public const string TurnEnd = "turn.end";
    public const string TurnFail = "turn.fail";

    public const string LeaseTake = "lease.take";
    public const string LeaseRelease = "lease.release";
    public const string Blocked = "blocked";
    public const string Unblocked = "unblocked";

    public const string BranchCreate = "branch.create";
    public const string BranchRebase = "branch.rebase";
    public const string PrOpen = "pr.open";
    public const string PrReady = "pr.ready";
    public const string PrClose = "pr.close";
}
