namespace Orchestrator.Web.Components.Tray;

/// <summary>The waiting cards by type, for the ring and the text of the tray badge.</summary>
public sealed record TrayCounts(int Questions, int Reviews, int Reports, int Blocked)
{
    public static readonly TrayCounts None = new(0, 0, 0, 0);

    public int Total => Questions + Reviews + Reports + Blocked;

    /// <summary>The counts of the cards of the board.</summary>
    public static TrayCounts Of(IReadOnlyCollection<CardView> cards) => new(
        cards.Count(c => c.Group == CardGroup.Questions),
        cards.Count(c => c.Group == CardGroup.Reviews),
        cards.Count(c => c.Group == CardGroup.Reports),
        cards.Count(c => c.Group == CardGroup.Blocked));

    /// <summary>The first line of the badge text.</summary>
    public string Headline => Total == 0 ? "Nothing waits" : $"{Total} waiting";

    /// <summary>The second line of the badge text: the counts by type.</summary>
    public string Detail
    {
        get
        {
            var parts = new[] { (Questions, "question"), (Reviews, "review"), (Reports, "report"), (Blocked, "blocked") }
                .Where(p => p.Item1 > 0)
                .Select(p => $"{p.Item1} {p.Item2}{(p.Item1 > 1 && p.Item2 != "blocked" ? "s" : "")}");
            var text = string.Join(" · ", parts);
            return text.Length > 0 ? text : "The inbox is clear";
        }
    }
}
