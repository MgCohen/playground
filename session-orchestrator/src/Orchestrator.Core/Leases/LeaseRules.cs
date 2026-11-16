using Orchestrator.Core.Contract;

namespace Orchestrator.Core.Leases;

public static class LeaseRules
{
    public static readonly TimeSpan LiveWindow = TimeSpan.FromSeconds(15);

    public static bool IsLive(Lease lease, DateTimeOffset? heartbeat, DateTimeOffset now)
    {
        if (lease.Holder != Holders.Live) return false;
        return IsRecent(heartbeat, now) || IsRecent(Times.Parse(lease.Since), now);
    }

    static bool IsRecent(DateTimeOffset? at, DateTimeOffset now) => at is { } time && now - time < LiveWindow;
}

public static class Times
{
    public static DateTimeOffset? Parse(string? text) =>
        DateTimeOffset.TryParse(text?.Trim(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    public static string Format(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
}
