using System.Globalization;

namespace Orchestrator.Store;

public static class Durations
{
    public static DateTimeOffset Since(string text, DateTimeOffset now)
    {
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) && text.Contains('-')) return at;
        return now - Parse(text);
    }

    public static TimeSpan Parse(string text)
    {
        text = text.Trim();
        if (text.Length < 2 || !double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            throw new FormatException($"bad duration {text}: use for example 30s, 10m, 3h or 2d");
        return char.ToLowerInvariant(text[^1]) switch
        {
            's' => TimeSpan.FromSeconds(n),
            'm' => TimeSpan.FromMinutes(n),
            'h' => TimeSpan.FromHours(n),
            'd' => TimeSpan.FromDays(n),
            _ => throw new FormatException($"bad duration {text}: use s, m, h or d"),
        };
    }
}
