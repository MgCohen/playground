using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.WebUtilities;
using NavSpike.Shared;

namespace Spike.SiteRouter.Nav;

/// <summary>
/// The one navigation controller of a circuit. Pages call it with types, not URLs.
/// It parses the URL into NavState, runs the page hooks, keeps the push stack and the list of visited URLs.
/// SiteHost (INavHost) renders what it decides.
/// </summary>
public sealed class Navigator(NavigationManager nav, PageTable pages, Trace trace) : IDisposable
{
    readonly List<PushEntry> stack = [];
    readonly List<string> visited = [];
    INavHost? host;
    IDisposable? guard;
    Move? pending;   // The kind of the next URL move, set by Go and Back.
    Move current;    // The kind of the URL move in progress.

    public NavState State { get; private set; } = default!;
    public IReadOnlyList<PushEntry> Stack => stack;

    /// <summary>The top push entry, or else the page type of the URL.</summary>
    public object? ActiveSlot => stack.Count > 0 ? stack[^1] : State.Page;
    Type? ActiveType => stack.Count > 0 ? stack[^1].Page : State.Page;

    public string UrlOf<TPage>() => pages.PathOf(typeof(TPage));

    /// <summary>The host calls it in OnInitialized (also in the prerender).</summary>
    public void Start(INavHost view)
    {
        host = view;
        State = Parse(nav.Uri, Move.Tab);
        visited.Add(State.Url);
        nav.LocationChanged += Changed;
    }

    /// <summary>The host calls it in the first OnAfterRender: the leave guard needs a live circuit.</summary>
    public void Attach() => guard ??= nav.RegisterLocationChangingHandler(Changing);

    /// <summary>Typed navigation: the URL comes from the page table.</summary>
    public void Go<TPage>(IReadOnlyDictionary<string, object?>? query = null) where TPage : IComponent
    {
        trace.Add($"Navigator: Go<{typeof(TPage).Name}>");
        var url = nav.ToAbsoluteUri(pages.PathOf(typeof(TPage)).TrimStart('/')).ToString();
        pending = Move.Push;
        nav.NavigateTo(query is null ? url : nav.GetUriWithQueryParameters(url, query));
    }

    /// <summary>Pushes a page with no URL and waits for its Close. The result comes back typed.</summary>
    public async Task<TResult?> PushAsync<TPage, TResult>(IDictionary<string, object>? parameters = null) where TPage : IComponent
    {
        trace.Add($"Navigator: PushAsync<{typeof(TPage).Name}, {typeof(TResult).Name}>");
        var c = new NavContext(Move.Push, ActiveType, typeof(TPage));
        if (!await LeaveAsync(c)) return default;
        var entry = new PushEntry(typeof(TPage), parameters ?? new Dictionary<string, object>());
        stack.Add(entry);
        await EnterAsync(c);
        var result = await entry.Done.Task;
        trace.Add($"Navigator: PushAsync result {result ?? "null"}");
        return result is TResult typed ? typed : default;
    }

    /// <summary>Closes the top pushed page and gives the result to its PushAsync.</summary>
    public async Task Close(object? result)
    {
        if (stack.Count == 0) return;
        trace.Add($"Navigator: Close({result ?? "null"})");
        var entry = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        entry.Done.TrySetResult(result);
        await EnterAsync(new NavContext(Move.Back, entry.Page, ActiveType));
    }

    /// <summary>Pops the push stack, or else goes to the previous URL in our own list.</summary>
    public async Task Back()
    {
        if (stack.Count > 0)
        {
            trace.Add("Navigator: Back -> Close(null)");
            await Close(null);
            return;
        }
        if (visited.Count < 2)
        {
            trace.Add("Navigator: Back (no history)");
            return;
        }
        trace.Add($"Navigator: Back -> {visited[^2]}");
        pending = Move.Back;
        nav.NavigateTo(nav.ToAbsoluteUri(visited[^2].TrimStart('/')).ToString());
    }

    // Before each URL move: a link click (Tab), Go (Push), Back, or the browser back button (Back).
    async ValueTask Changing(LocationChangingContext context)
    {
        try
        {
            current = pending ?? (context.IsNavigationIntercepted ? Move.Tab : Move.Back);
            pending = null;
            var to = Parse(context.TargetLocation, current);
            trace.Add($"Navigator: LocationChanging {current} -> {to.Url}");
            if (to.Url == State.Url) return;
            if (!await LeaveAsync(new NavContext(current, ActiveType, to.Page)))
            {
                trace.Add("Navigator: PreventNavigation");
                context.PreventNavigation();
            }
        }
        catch (Exception e)
        {
            // An exception here ends the circuit: log it and let the move continue.
            trace.Add($"Navigator: Changing error {e.Message}");
        }
    }

    // After each URL move: a new NavState, the motion, then OnEnterAsync.
    async void Changed(object? sender, LocationChangedEventArgs e)
    {
        try
        {
            var from = ActiveType;
            var to = Parse(e.Location, current);
            if (to.Url == State.Url) return;
            State = to;
            if (current == Move.Back && visited.Count > 1 && visited[^2] == to.Url) visited.RemoveAt(visited.Count - 1);
            else visited.Add(to.Url);
            // A URL move closes all pushed pages with no result.
            foreach (var entry in stack) entry.Done.TrySetResult(null);
            stack.Clear();
            trace.Add($"Navigator: LocationChanged {current} -> {to.Url}");
            await EnterAsync(new NavContext(current, from, to.Page));
        }
        catch (Exception ex)
        {
            trace.Add($"Navigator: Changed error {ex.Message}");
        }
    }

    async Task<bool> LeaveAsync(NavContext c) => host?.Instance(ActiveSlot) is not { } page || await page.OnLeaveAsync(c);

    async Task EnterAsync(NavContext c)
    {
        if (host is null) return;
        await host.PlayAsync(c.Move);
        if (host.Instance(ActiveSlot) is { } page) await page.OnEnterAsync(c);
    }

    NavState Parse(string location, Move move)
    {
        var uri = nav.ToAbsoluteUri(location);
        var url = "/" + nav.ToBaseRelativePath(uri.ToString());
        var path = uri.AbsolutePath.Length > 1 ? "/" + nav.ToBaseRelativePath(uri.GetLeftPart(UriPartial.Path)).TrimEnd('/') : "/";
        var query = QueryHelpers.ParseQuery(uri.Query).ToDictionary(p => p.Key, p => p.Value.ToString());
        return new NavState(url, path, pages.Find(path), move, query);
    }

    public void Dispose()
    {
        guard?.Dispose();
        nav.LocationChanged -= Changed;
    }
}
