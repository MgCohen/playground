namespace Orchestrator.Web;

/// <summary>One part of the header path: a title and the address of its page.</summary>
public sealed record Crumb(string Title, string Href);

/// <summary>
/// T3: the path of the sub-pages in the header, such as Sessions › orch-bad3 › Stream › Question.
/// A tab page (Home, Cards, Stream, Sessions, Settings) starts a new path. A sub-page adds its part; a page that is already
/// in the path cuts the path back to it. A sub-page opened directly (a link, a reload) uses its default parent.
/// One instance for each user circuit (scoped).
/// </summary>
public sealed class PathTrail
{
    readonly List<Crumb> parts = [];

    /// <summary>The parts before the current page.</summary>
    public IReadOnlyList<Crumb> Parents => parts.Count > 1 ? parts.Take(parts.Count - 1).ToList() : [];

    /// <summary>Enters a page and returns the parts before it.</summary>
    /// <param name="page">The current page.</param>
    /// <param name="tab">True for a tab page of the app bar: the path starts again.</param>
    /// <param name="parent">The default parent of a sub-page, used when the path does not lead to it.</param>
    public IReadOnlyList<Crumb> Enter(Crumb page, bool tab, Crumb? parent = null)
    {
        if (tab) parts.Clear();
        var at = parts.FindIndex(p => Same(p.Href, page.Href));
        if (at >= 0) parts.RemoveRange(at, parts.Count - at);
        else if (!tab && parts.Count == 0 && parent is not null) parts.Add(parent);
        parts.Add(page);
        return Parents;
    }

    static bool Same(string a, string b) => string.Equals(a.TrimStart('/'), b.TrimStart('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The parents to show: with more than two parents, the first ones become one "…" part that leads to the last hidden page,
    /// so the last two parents always show in full.
    /// </summary>
    public static IReadOnlyList<Crumb> Shown(IReadOnlyList<Crumb> parents) =>
        parents.Count <= 2 ? parents : [new Crumb("…", parents[^3].Href), parents[^2], parents[^1]];
}
