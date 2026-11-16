using PatternEngine.Features.Trace.Domain.Shared;

namespace PatternEngine.Features.Engine.Domain.Shared;

/// <summary>
/// The way back to the user. The engine asks when it does not have enough information. The answer is free text;
/// null means that there is no answer (no user, or "no preference").
/// </summary>
public interface IUserChannel
{
    Task<string?> AskAsync(Question question, CancellationToken ct);
}

/// <summary>No user: each question stays open, and the run ends with the outcome "ask".</summary>
public sealed class NoUser : IUserChannel
{
    public Task<string?> AskAsync(Question question, CancellationToken ct) => Task.FromResult<string?>(null);
}

public static class Answers
{
    public const string NoPreference = "no preference";

    public static bool IsEmpty(string? answer) =>
        string.IsNullOrWhiteSpace(answer) || string.Equals(answer.Trim(), NoPreference, StringComparison.OrdinalIgnoreCase);
}
