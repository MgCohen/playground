using Orchestrator.Core.Contract;
using Orchestrator.Core.Leases;

namespace Orchestrator.Core.Policy;

public static class DefaultRule
{
    public static bool IsDue(Decision decision, Level level, DateTimeOffset now)
    {
        if (level.TimeoutSeconds is not { } timeout || string.IsNullOrEmpty(decision.DefaultOption)) return false;
        var asked = Times.Parse(decision.Asked) ?? DateTimeOffset.MinValue;
        return now - asked >= TimeSpan.FromSeconds(timeout);
    }

    public static string Reason(Level level) => $"level {level.Name}, no answer in {level.TimeoutSeconds} s";
}
