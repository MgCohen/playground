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

public sealed record SupervisorOptions(int MaxRuns, int MaxFailures, int MaxChecks, Core.Policy.Policy Policy)
{
    public TimeSpan RetryAfter { get; init; } = TimeSpan.FromMinutes(5);
    public Action<string>? Import { get; init; }
    public TimeSpan ImportWait { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan FirstReminder { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan NextReminder { get; init; } = TimeSpan.FromHours(1);
}

static class Data
{
    public static Dictionary<string, object?> Of(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);
}
