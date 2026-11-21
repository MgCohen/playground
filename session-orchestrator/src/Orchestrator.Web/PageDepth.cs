using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Orchestrator.Web;

/// <summary>
/// The depth of a page: 0 for a tab page of the app bar (the default), more for a page below it. The motion host compares the
/// depth of two pages to pick the page move. A query parameter can give the page another depth, for example a stream of one
/// session is below the stream of all sessions.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PageDepthAttribute(double depth) : Attribute
{
    public double Depth { get; } = depth;

    /// <summary>A query parameter that gives the page the depth <see cref="QueryDepth"/> when it is in the address.</summary>
    public string? Query { get; init; }

    public double QueryDepth { get; init; }
}

/// <summary>The page move between two addresses, from the routes and the <see cref="PageDepthAttribute"/> of the pages.</summary>
public static class PageDepth
{
    public const string Tab = "tab", Push = "push", Back = "back";

    sealed record Route(string[] Parts, PageDepthAttribute? Depth);

    static readonly Route[] Routes = typeof(PageDepth).Assembly.GetTypes()
        .SelectMany(t => t.GetCustomAttributes<RouteAttribute>().Select(r => new Route(Split(r.Template), t.GetCustomAttribute<PageDepthAttribute>())))
        .ToArray();

    static string[] Split(string path) => path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The depth of a base-relative address (for example "events/e1" or "stream?session=s1"), or null for no page.</summary>
    public static double? Of(string relative)
    {
        var at = relative.IndexOfAny(['?', '#']);
        var path = Split(at < 0 ? relative : relative[..at]);
        var query = at < 0 || relative[at] != '?' ? "" : relative[(at + 1)..].Split('#')[0];
        foreach (var route in Routes)
        {
            if (route.Parts.Length != path.Length) continue;
            if (!route.Parts.Zip(path).All(p => p.First.StartsWith('{') || string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase))) continue;
            if (route.Depth is not { } d) return 0;
            var keys = query.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(q => Uri.UnescapeDataString(q.Split('=')[0]));
            return d.Query is { } name && keys.Contains(name, StringComparer.OrdinalIgnoreCase) ? d.QueryDepth : d.Depth;
        }
        return null;
    }

    /// <summary>
    /// The move from one address to another: a tab change between two tab pages, a push to a deeper page, a back to a page that is
    /// less deep, or null for no move (the same address, two pages at the same depth below the tabs, or an address with no page).
    /// </summary>
    public static string? Move(string from, string to)
    {
        if (string.Equals(from.Split('#')[0], to.Split('#')[0], StringComparison.OrdinalIgnoreCase)) return null;
        if (Of(from) is not { } a || Of(to) is not { } b) return null;
        if (a == 0 && b == 0) return Tab;
        return b > a ? Push : b < a ? Back : null;
    }
}
