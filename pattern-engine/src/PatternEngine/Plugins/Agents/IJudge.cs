using System.Text.Json;

namespace PatternEngine.Plugins.Agents;

/// <summary>One agent call: the agent file name, the input text and the folder that the call runs in.</summary>
public sealed record AgentCall(string Agent, string Input, string WorkDir);

public sealed record AgentAnswer(JsonElement? Data, long Ms, double Cost, string? Error)
{
    public bool Ok => Data is not null && Error is null;

    public string? Text(string field) =>
        Field(field) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    public IReadOnlyList<string> Texts(string field) =>
        Field(field) is { ValueKind: JsonValueKind.Array } v
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];

    public IReadOnlyDictionary<string, string> Map(string field) =>
        Field(field) is { ValueKind: JsonValueKind.Object } v
            ? v.EnumerateObject().Where(p => p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                .ToDictionary(p => p.Name, p => p.Value.ToString(), StringComparer.Ordinal)
            : new Dictionary<string, string>();

    public JsonElement? Field(string field) =>
        Data is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty(field, out var v) ? v : null;
}

/// <summary>The model boundary. Each judgment is one agent call. Implementations: CliJudge (claude -p), ScriptedJudge (tests).</summary>
public interface IJudge
{
    Task<AgentAnswer> CallAsync(AgentCall call, CancellationToken ct);

    /// <summary>The version of one agent as a checker: a change of the agent file or the model gives another version.</summary>
    string Version(string agent) => "none";
}
