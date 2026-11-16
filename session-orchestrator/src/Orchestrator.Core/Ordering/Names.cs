namespace Orchestrator.Core.Ordering;

public sealed class Names : IComparer<string>
{
    public static readonly Names Order = new();

    public static string NewInboxName(DateTimeOffset at) => $"{at.ToUnixTimeMilliseconds()}-{Guid.NewGuid().ToString()[..8]}.txt";

    public static string NewEventId(DateTimeOffset at, string writer) => $"{at.ToUnixTimeMilliseconds()}-{Guid.NewGuid().ToString()[..8]}-{writer}";

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null) return string.CompareOrdinal(x, y);
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var (a, nextI) = Run(x, i);
                var (b, nextJ) = Run(y, j);
                var byNumber = CompareNumbers(a, b);
                if (byNumber != 0) return byNumber;
                i = nextI;
                j = nextJ;
                continue;
            }
            var byChar = char.ToLowerInvariant(x[i]).CompareTo(char.ToLowerInvariant(y[j]));
            if (byChar != 0) return byChar;
            i++;
            j++;
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }

    static (string Digits, int Next) Run(string text, int start)
    {
        var end = start;
        while (end < text.Length && char.IsDigit(text[end])) end++;
        return (text[start..end], end);
    }

    static int CompareNumbers(string a, string b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
    }
}
