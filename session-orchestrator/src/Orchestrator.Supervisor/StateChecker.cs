using System.Text.Json;
using System.Text.RegularExpressions;

namespace Orchestrator.Supervisor;

/// <summary>The fallback of the state machine: one model call of the provider with the read-only agent state-checker of the orch plugin.</summary>
public sealed partial class StateChecker(IProvider provider) : IStateChecker
{
    public async Task<StateCheckResult> Check(StateCheckRequest request, CancellationToken cancel)
    {
        var prompt = string.Join('\n',
            $"Session: {request.Id}",
            $"State before: {request.Current ?? "none"}",
            request.Conflict.Count > 0 ? $"Conflict: the transitions {string.Join(" and ", request.Conflict)} both say yes with the same priority." : "Conflict: none. The state file was missing or not valid.",
            $"Valid states: {string.Join(", ", Core.Lifecycle.States.All)}.",
            "Snapshot of the store (JSON):",
            request.Snapshot,
            "Reply with only this JSON: {\"state\": \"<one valid state>\", \"reason\": \"<one sentence>\"}.");
        var output = await provider.Ask(new AskRequest(request.Cwd, prompt, 4) { Agent = "state-checker", Plugin = true }, cancel);
        if (!output.Ok) return new StateCheckResult(null, output.Error ?? "the state checker failed");
        return ParseReply(output.Text ?? "");
    }

    public static StateCheckResult ParseReply(string text)
    {
        var match = Braces().Match(text);
        if (!match.Success) return new StateCheckResult(null, "no JSON in the reply");
        try
        {
            var value = JsonSerializer.Deserialize<JsonElement>(match.Value);
            var state = value.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            var reason = value.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
            return new StateCheckResult(Core.Lifecycle.States.IsKnown(state) ? state : null, reason);
        }
        catch (JsonException)
        {
            return new StateCheckResult(null, "the reply is not valid JSON");
        }
    }

    [GeneratedRegex(@"\{[\s\S]*\}")]
    private static partial Regex Braces();
}
