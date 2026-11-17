using Orchestrator.Core.Contract;
using Orchestrator.Core.Leases;

namespace Orchestrator.Core.Policy;

public static class DefaultRule
{
    public static bool IsDue(Decision decision, Level level, DateTimeOffset now) => At(decision, level) is { } at && now >= at;

    /// <summary>The time when the policy answers with the default. None when the decision has no default or the level never times out.</summary>
    public static DateTimeOffset? At(Decision decision, Level level)
    {
        if (level.TimeoutSeconds is not { } timeout || string.IsNullOrEmpty(decision.DefaultOption)) return null;
        var asked = Times.Parse(decision.Asked) ?? DateTimeOffset.MinValue;
        return asked == DateTimeOffset.MinValue ? asked : asked + TimeSpan.FromSeconds(timeout);
    }

    public static string Reason(Level level) => $"level {level.Name}, no answer in {level.TimeoutSeconds} s";
}
