namespace Orchestrator.Core.Events;

public static class EventKinds
{
    public const string Brief = "brief";
    public const string Message = "message";
    public const string Decision = "decision";
    public const string Query = "query";
    public const string Plan = "plan";
    public const string Answer = "answer";
    public const string Review = "review";
    public const string Report = "report";
    public const string Update = "update";
    public const string Notice = "notice";

    public const string Ack = "ack";

    public const string SessionInput = "session.input";
    public const string SessionOutput = "session.output";
    public const string SessionTool = "session.tool";
    public const string TurnStart = "turn.start";
    public const string TurnEnd = "turn.end";
    public const string TurnFail = "turn.fail";

    public const string SessionStart = "session.start";
    public const string SessionStop = "session.stop";
    public const string SessionResume = "session.resume";
    public const string SessionArchive = "session.archive";
    public const string SessionImport = "session.import";
    public const string SessionEnd = "session.end";
    public const string LeaseTake = "lease.take";
    public const string LeaseRelease = "lease.release";
    public const string Blocked = "blocked";
    public const string DeliveryBlocked = "delivery.blocked";
    public const string SessionState = "session.state";
    public const string StateConflict = "state.conflict";
    public const string MetaUpdate = "meta.update";

    public const string BranchCreate = "branch.create";
    public const string BranchRebase = "branch.rebase";
    public const string PrOpen = "pr.open";
    public const string PrReady = "pr.ready";
    public const string PrClose = "pr.close";
    public const string PrMerge = "pr.merge";
    public const string MergeBlock = "merge.block";

    /// <summary>The message kinds: each has a template, and a receiver acknowledges each delivery.</summary>
    public static readonly IReadOnlySet<string> Messages = new HashSet<string> { Brief, Message, Decision, Query, Plan, Answer, Review, Report, Update, Notice };

    /// <summary>The message kinds that a session gets as a turn. An update to a session is for its tree only.</summary>
    public static readonly IReadOnlySet<string> Deliverable = new HashSet<string> { Brief, Message, Decision, Query, Plan, Answer, Review, Report, Notice };

    /// <summary>The kinds that ask a party for an answer.</summary>
    public static readonly IReadOnlySet<string> Questions = new HashSet<string> { Decision, Query };
}
