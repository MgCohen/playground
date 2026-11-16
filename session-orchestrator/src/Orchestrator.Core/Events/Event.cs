using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Orchestrator.Core.Events;

/// <summary>
/// One envelope of the stream. The body file holds the content. <see cref="Thread"/> groups one conversation,
/// and <see cref="Needs"/> marks an envelope that waits for an act of the receiver.
/// </summary>
public sealed record Event(string Id, string At, string? Session, string Kind, string From, string To, string Subject, string Body, IReadOnlyDictionary<string, JsonElement> Refs)
{
    public string? Thread { get; init; }
    public string? Needs { get; init; }

    public string? Ref(string key) => Refs.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public IReadOnlyList<string> RefList(string key) =>
        Refs.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(v => v.GetString()).OfType<string>().ToList() : [];

    /// <summary>The envelope that this envelope forwards or acknowledges.</summary>
    [JsonIgnore]
    public string? Of => Ref(RefKeys.Of);

    /// <summary>The envelope that this envelope answers.</summary>
    [JsonIgnore]
    public string? Reply => Ref(RefKeys.Reply);

    [JsonIgnore]
    public string? Via => Ref(RefKeys.Via);

    [JsonIgnore]
    public bool IsForward => Of is not null && Kind != EventKinds.Ack;
}

public static class RefKeys
{
    public const string Reply = "reply";
    public const string Of = "of";
    public const string Via = "via";
}

public static class Needs
{
    public const string Answer = "answer";
    public const string Review = "review";
    public const string Ack = "ack";
}

public static partial class Parties
{
    public const string User = "user";
    public const string Supervisor = "supervisor";
    public const string Docs = "docs";
    public const string Policy = "policy";
    public const string SessionPrefix = "session:";

    public static string Session(string id) => $"{SessionPrefix}{id}";

    public static string? SessionId(string party) => party.StartsWith(SessionPrefix, StringComparison.Ordinal) ? party[SessionPrefix.Length..] : null;

    /// <summary>The class of an address: <c>session</c> for <c>session:&lt;id&gt;</c>, else the address.</summary>
    public static string ClassOf(string party) => party.StartsWith(SessionPrefix, StringComparison.Ordinal) ? "session" : party;

    /// <summary>A file name for an address. Windows refuses ':' in a file name, so it becomes '-'.</summary>
    public static string Folder(string party) => party.Replace(':', '-');
}

/// <summary>The one subject rule. The plugin (events.ts) has the same rule, and the contract tests check both.</summary>
public static partial class Subjects
{
    public const int MaxLength = 80;
    static readonly string[] Fields = ["question", "summary", "title", "text"];

    public static string Of(string kind, IReadOnlyDictionary<string, object?> data)
    {
        if (kind == EventKinds.Answer && data.TryGetValue("text", out var answer)) return Clean(answer?.ToString() ?? "");
        if (kind == EventKinds.Review && data.TryGetValue("verdict", out var verdict)) return Clean($"{verdict}{(data.TryGetValue("text", out var said) && said is string { Length: > 0 } s ? $": {s}" : "")}");
        if (kind == EventKinds.Ack && data.TryGetValue("state", out var state)) return Clean($"{state} ({(data.TryGetValue("by", out var by) ? by : "")})");
        if (kind == EventKinds.Notice && data.TryGetValue("kind", out var notice)) return Clean($"{notice}{(data.TryGetValue("question", out var q) && q is string { Length: > 0 } question ? $": {question}" : "")}");
        return Of(data);
    }

    public static string Of(IReadOnlyDictionary<string, object?> data)
    {
        foreach (var field in Fields)
            if (data.TryGetValue(field, out var value) && value is not null)
                return Clean(value.ToString() ?? "");
        return "";
    }

    public static string Clean(string text)
    {
        var line = Space().Replace(text, " ").Trim();
        return line.Length > MaxLength ? line[..MaxLength] : line;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();
}
