using System.Text.Json;

namespace PatternEngine.Plugins.Agents;

/// <summary>For tests: a function gives the answer of each agent call. No model.</summary>
public sealed class ScriptedJudge(Func<AgentCall, object?> answer) : IJudge
{
    public List<AgentCall> Calls { get; } = [];

    public Task<AgentAnswer> CallAsync(AgentCall call, CancellationToken ct)
    {
        lock (Calls)
        {
            Calls.Add(call);
        }

        var data = answer(call);
        if (data is null)
        {
            return Task.FromResult(new AgentAnswer(null, 0, 0, "the script gave no answer"));
        }

        var element = data is JsonElement e ? e : JsonSerializer.SerializeToElement(data);
        return Task.FromResult(new AgentAnswer(element, 0, 0, null));
    }
}
