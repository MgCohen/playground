namespace Orchestrator.Web.Components.Tray;

/// <summary>
/// The rule of the badge notice (N2): a card gets one notice, only when it arrives while the tray is closed (hidden or at the
/// badge). Each card that the tray has seen is done: the cards at the start, the cards that arrive while the panel shows, and
/// each card after its notice. So a card never gets a second notice, and a minimize brings no notice for the cards that are
/// already in the app.
/// </summary>
public sealed class TrayNotices
{
    readonly HashSet<string> done = new(StringComparer.Ordinal);
    bool started;

    /// <summary>The new cards that get a notice now: the cards that are not done, if the tray is closed. All cards are done after the call.</summary>
    public IReadOnlyList<CardView> Take(IEnumerable<CardView> cards, bool closed)
    {
        var fresh = cards.Where(c => done.Add(c.Id)).ToList();
        var first = !started;
        started = true;
        return first || !closed ? [] : fresh;
    }
}
