using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using NavSpike.Shared;

namespace Spike.PageHostNav.Components.Navigation;

/// <summary>The kind of a move. The host selects the motion from it.</summary>
public enum NavKind { Push, Back, Tab }

/// <summary>The data of one move. A URL page has its path ("/counter"). A pushed page has its type name ("Picker").</summary>
public sealed record NavContext(NavKind Kind, string From, string To, CancellationToken Cancel = default);

/// <summary>Optional page hooks. A page that does not implement this interface still works.</summary>
public interface INavPage
{
    /// <summary>Runs after the motion, when the page is on screen.</summary>
    Task OnEnterAsync(NavContext c) => Task.CompletedTask;

    /// <summary>Runs before the move. Return false to cancel the move.</summary>
    Task<bool> OnLeaveAsync(NavContext c) => Task.FromResult(true);
}

/// <summary>One rendered page in the host: a kept URL page or a pushed page.</summary>
public sealed class PageSlot(Type type, IDictionary<string, object> values)
{
    public Type Type { get; } = type;
    public IDictionary<string, object> Values { get; set; } = values;
    public DynamicComponent? View { get; set; }
    public INavPage? Page => View?.Instance as INavPage;

    // The result of a pushed page. The continuation runs later, so CloseAsync does not run the caller code inline.
    internal TaskCompletionSource<object?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// The one central controller of navigation (one for each circuit). It decides the move kind,
/// calls the leave hook, keeps the push stack and the list of visited URLs, and tells the host to play the move.
/// </summary>
public sealed class Navigator(NavigationManager nav, IJSRuntime js, Trace trace)
{
    readonly List<PageSlot> stack = [];
    readonly List<string> visited = [];
    NavKind? nextKind;
    NavContext? move;
    PageHost? host;
    IDisposable? handler;

    /// <summary>The pushed pages. The last one is on top.</summary>
    public IReadOnlyList<PageSlot> Stack => stack;

    /// <summary>The page on top: the last pushed page, else the URL path.</summary>
    public string Current => stack.Count > 0 ? stack[^1].Type.Name : PathOf(nav.Uri);

    /// <summary>Goes to a URL page as a Push move.</summary>
    public void Push(string url)
    {
        trace.Add($"Navigator: Push({url})");
        nextKind = NavKind.Push;
        nav.NavigateTo(url);
    }

    /// <summary>Shows a page with no URL over the current page. Returns the value that the page gives to CloseAsync.</summary>
    public async Task<TResult?> PushAsync<TPage, TResult>(IDictionary<string, object>? parameters = null) where TPage : IComponent
    {
        var ctx = new NavContext(NavKind.Push, Current, typeof(TPage).Name);
        trace.Add($"Navigator: PushAsync<{ctx.To}>");
        if (!await LeaveAsync(ctx)) return default;
        var slot = new PageSlot(typeof(TPage), parameters ?? new Dictionary<string, object>());
        stack.Add(slot);
        if (host is not null) await host.PlayAsync(ctx);
        var result = await slot.Result.Task;
        trace.Add($"Navigator: PushAsync<{ctx.To}> result {result ?? "null"}");
        return result is TResult typed ? typed : default;
    }

    /// <summary>Closes the pushed page on top and gives the result to its caller.</summary>
    public async Task CloseAsync(object? result)
    {
        if (stack.Count == 0) return;
        trace.Add($"Navigator: CloseAsync({result ?? "null"})");
        await PopAsync(result);
    }

    /// <summary>Pops the pushed page on top. If the stack is empty, goes back in the URL history.</summary>
    public async Task BackAsync()
    {
        if (stack.Count > 0)
        {
            var ctx = new NavContext(NavKind.Back, Current, stack.Count > 1 ? stack[^2].Type.Name : PathOf(nav.Uri));
            trace.Add($"Navigator: BackAsync pop {ctx.From}");
            if (await LeaveAsync(ctx)) await PopAsync(null);
            return;
        }
        trace.Add("Navigator: BackAsync history.back");
        nextKind = NavKind.Back;
        await js.InvokeVoidAsync("history.back");
    }

    internal void Attach(PageHost h)
    {
        host = h;
        visited.Add(PathOf(nav.Uri));
        handler = nav.RegisterLocationChangingHandler(OnLocationChangingAsync);
    }

    internal void Detach()
    {
        handler?.Dispose();
        host = null;
    }

    /// <summary>Gives the host the move of the URL change. The first page of the circuit has no move: it is a Tab.</summary>
    internal NavContext TakeMove()
    {
        var m = move ?? new NavContext(NavKind.Tab, "", PathOf(nav.Uri));
        move = null;
        return m;
    }

    // An exception here ends the circuit, so the handler catches all exceptions.
    async ValueTask OnLocationChangingAsync(LocationChangingContext c)
    {
        try
        {
            var to = PathOf(nav.ToAbsoluteUri(c.TargetLocation).ToString());
            // A link click is a Tab. A popstate to the previous URL is Back (also the browser back button).
            var kind = nextKind ?? (c.IsNavigationIntercepted ? NavKind.Tab
                : visited.Count > 1 && visited[^2] == to ? NavKind.Back : NavKind.Tab);
            nextKind = null;
            var ctx = new NavContext(kind, Current, to, c.CancellationToken);
            trace.Add($"Navigator: LocationChanging {kind} {ctx.From} -> {to}");
            // A move to the same page has no leave hook.
            if (stack.Count == 0 && to == ctx.From)
            {
                move = ctx with { Cancel = default };
                return;
            }
            if (!await LeaveAsync(ctx))
            {
                trace.Add("Navigator: PreventNavigation");
                c.PreventNavigation();
                return;
            }
            // A URL move closes all pushed pages. Their callers get no result.
            foreach (var slot in stack) slot.Result.TrySetResult(null);
            stack.Clear();
            if (kind == NavKind.Back && visited.Count > 1) visited.RemoveAt(visited.Count - 1);
            else visited.Add(to);
            move = ctx with { Cancel = default };
        }
        catch (Exception e)
        {
            trace.Add($"Navigator: error {e.GetType().Name}: {e.Message}");
        }
    }

    async Task<bool> LeaveAsync(NavContext ctx)
    {
        var page = stack.Count > 0 ? stack[^1].Page : host?.Active?.Page;
        return page is null || await page.OnLeaveAsync(ctx);
    }

    async Task PopAsync(object? result)
    {
        var slot = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        slot.Result.TrySetResult(result);
        if (host is not null) await host.PlayAsync(new NavContext(NavKind.Back, slot.Type.Name, Current));
    }

    string PathOf(string uri) => "/" + nav.ToBaseRelativePath(uri).Split('?', '#')[0];
}
