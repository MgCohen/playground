namespace Orchestrator.Supervisor;

public sealed record TurnRequest(string Id, string Cwd, string Prompt, bool IsNew)
{
    public string? Agent { get; init; }
    public string? Title { get; init; }
}

public sealed record TurnResult(bool Ok, string? Text, string? Error, double Seconds);

public interface ITurnRunner
{
    Task<TurnResult> Run(TurnRequest request, CancellationToken cancel);
}

public sealed record DocsRequest(string Cwd, string Question, IReadOnlyList<string> Options, IReadOnlyList<string> Docs);

public sealed record DocsResult(bool Answered, string? Answer = null, string? Source = null);

public interface IDocsChecker
{
    Task<DocsResult> Check(DocsRequest request, CancellationToken cancel);
}

/// <summary>The input of the fallback agent: the snapshot of one session as JSON, the state before, and the conflict.</summary>
public sealed record StateCheckRequest(string Id, string Cwd, string Snapshot, string? Current, IReadOnlyList<string> Conflict);

/// <summary>The state that the fallback agent inferred, and its reason. A null state: the agent gave no valid state.</summary>
public sealed record StateCheckResult(string? State, string Reason);

/// <summary>The fallback of the state machine: an agent infers the state when no check matches or two guards conflict.</summary>
public interface IStateChecker
{
    Task<StateCheckResult> Check(StateCheckRequest request, CancellationToken cancel);
}

public sealed record SupervisorOptions(int MaxRuns, int MaxFailures, int MaxChecks, Core.Policy.Policy Policy)
{
    public TimeSpan RetryAfter { get; init; } = TimeSpan.FromMinutes(5);
    public Action<string>? Import { get; init; }
    public TimeSpan ImportWait { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan FirstReminder { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan NextReminder { get; init; } = TimeSpan.FromHours(1);

    /// <summary>The fallback agent. Null: an unknown state stays unknown and goes to the user.</summary>
    public IStateChecker? StateChecker { get; init; }

    /// <summary>The wait before the fallback agent runs again for the same session.</summary>
    public TimeSpan FallbackWait { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>False: <see cref="Supervisor.Tick"/> does nothing. A view process on the store of a running supervisor (the tray in live mode) uses only the actions.</summary>
    public bool Ticks { get; init; } = true;
}

static class Data
{
    public static Dictionary<string, object?> Of(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);
}
