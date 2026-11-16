using System.Diagnostics;
using System.Text.Json.Serialization;
using PatternEngine.Plugins.Agents;

namespace PatternEngine.Features.Engine.Domain;

public sealed record AgentLogEntry(
    [property: JsonPropertyName("agent")] string Agent,
    [property: JsonPropertyName("ms")] long Ms,
    [property: JsonPropertyName("cost")] double Cost,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("data")] object? Data);

/// <summary>
/// The one way from the engine to the judge. It counts the agent calls, the cost and the time, and it stops
/// a call when a run limit is used up. The stop reason goes to the trace.
/// </summary>
public sealed class AgentGateway(IJudge judge, string workDir, RunLimits limits, TimeProvider? clock = null)
{
    private readonly Stopwatch watch = Stopwatch.StartNew();
    private readonly object gate = new();

    public List<AgentLogEntry> Log { get; } = [];

    public int Calls { get; private set; }

    public double Cost { get; private set; }

    public string? StopReason { get; private set; }

    public TimeProvider Clock { get; } = clock ?? TimeProvider.System;

    public bool Stopped => StopReason is not null;

    public async Task<AgentAnswer> CallAsync(string agent, string input, CancellationToken ct)
    {
        lock (gate)
        {
            StopReason ??= Calls >= limits.AgentCalls ? $"run limit: {limits.AgentCalls} agent calls"
                : Cost >= limits.CostUsd ? $"run limit: cost {limits.CostUsd} USD"
                : watch.Elapsed.TotalSeconds >= limits.Seconds ? $"run limit: {limits.Seconds} s"
                : null;
            if (StopReason is not null)
            {
                return new AgentAnswer(null, 0, 0, StopReason);
            }

            Calls++;
        }

        var answer = await judge.CallAsync(new AgentCall(agent, input, workDir), ct);
        lock (gate)
        {
            Cost += answer.Cost;
            Log.Add(new AgentLogEntry(agent, answer.Ms, answer.Cost, answer.Error, answer.Data));
        }

        return answer;
    }

    public long ElapsedMs => watch.ElapsedMilliseconds;
}
