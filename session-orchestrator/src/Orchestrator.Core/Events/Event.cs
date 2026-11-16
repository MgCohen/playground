using System.Text.Json;
using System.Text.RegularExpressions;

namespace Orchestrator.Core.Events;

public sealed record Event(string Id, string At, string? Session, string Kind, string From, string To, string Subject, string Body, IReadOnlyDictionary<string, JsonElement> Refs);

public static partial class Parties
{
    public const string User = "user";
    public const string Supervisor = "supervisor";
    public const string Docs = "docs";
    public const string Policy = "policy";

    public static string Session(string id) => $"session:{id}";
}

public static partial class Subjects
{
    public const int MaxLength = 80;
    static readonly string[] Fields = ["question", "summary", "title", "text"];

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
