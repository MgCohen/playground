using Orchestrator.Core.Events;

namespace Orchestrator.Web;

/// <summary>The settings of the filter and order sheet. Cards and Stream use the same sheet.</summary>
public sealed class ViewFilter
{
    public static readonly string[] Orders = ["Arrival", "Newest", "Session", "Default time"];
    public static readonly string[] Dates = ["Today", "7 days", "All"];
    public static readonly string[] Sources = ["Sessions", "Policy", "Docs", "Supervisor", "User"];

    public string Order { get; set; } = "Arrival";
    public string Date { get; set; } = "All";
    public HashSet<string> HiddenSessions { get; } = [];
    public HashSet<string> HiddenSources { get; } = [];

    public bool Active => Order != "Arrival" || Date != "All" || HiddenSessions.Count > 0 || HiddenSources.Count > 0;

    public void Reset()
    {
        Order = "Arrival";
        Date = "All";
        HiddenSessions.Clear();
        HiddenSources.Clear();
    }

    public static string Source(Event e) => e.From switch
    {
        var f when f.StartsWith(Parties.SessionPrefix) => "Sessions",
        Parties.Policy => "Policy",
        Parties.Docs => "Docs",
        Parties.User => "User",
        _ => "Supervisor",
    };

    /// <summary>The filters of the date, the session (the root of its tree) and the source.</summary>
    public bool Keeps(Event e, string? root, DateTimeOffset now)
    {
        if (HiddenSources.Contains(Source(e))) return false;
        if (root is not null && HiddenSessions.Contains(root)) return false;
        if (Date == "All" || Ui.Time(e) is not { } at) return true;
        return Date == "Today" ? at.ToLocalTime().Date == now.ToLocalTime().Date : now - at <= TimeSpan.FromDays(7);
    }
}
