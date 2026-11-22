# 08 siterouter: our shape, no Router (.NET 10)

| Item | Value |
|---|---|
| Package | None. `NavigationManager`, `INavigationInterception`, `DynamicComponent` |
| Setup | `Nav/NavState.cs`, `Nav/Navigator.cs`, `Components/SiteHost.razor` (+ `.razor.css`), 3 pages, `Program.cs` with `AddNav` and `MapFallback` |
| Lines of spike code | 432 (all files in this folder, no empty lines, without the shared base) |

The line count by file: `Navigator.cs` 148, `SiteHost.razor` 76, `NavState.cs` 60, `Counter.razor` 42, `Home.razor` 33, `Program.cs` 19, `Picker.razor` 18, `App.razor` 15, `SiteHost.razor.css` 9, `_Imports.razor` 7, csproj 5.

| Capability | Result | How |
|---|---|---|
| Swap pages | yes | `SiteHost` renders each kept page type with `DynamicComponent` and `@key`, and hides the inactive pages |
| Transition | yes | CSS classes `push`, `back`, `tab` in `SiteHost.razor.css`, 300 ms. `PlayAsync` waits with `Task.Delay(300)` |
| Direction (push/back) | yes | `Move` comes from `Navigator`: `Go` = Push, `Back` = Back, intercepted link = Tab, other = Back (browser button) |
| Lifecycle hooks | yes | `INavPage.OnLeaveAsync`, `INavPage.OnEnterAsync` (optional, default interface methods), plus `TracedPage` hooks |
| Cancel a leave | yes | `RegisterLocationChangingHandler` calls `OnLeaveAsync`. False calls `context.PreventNavigation()` |
| Keep-alive (F5) | yes | One instance for each page type, hidden with `hidden`, not disposed |
| Typed back result | yes | `PushAsync<Picker, string>()` awaits a `TaskCompletionSource`. `Navigator.Close("Red")` sets it |
| Shared state (F4) | yes | `CounterState` (scoped). The new `NavState` cascade renders the kept Counter again |

## Public API

```csharp
public enum Move { Tab, Push, Back }
public sealed record NavState(string Url, string Path, Type? Page, Move Move, IReadOnlyDictionary<string, string> Query);
public sealed record NavContext(Move Move, Type? From, Type? To);

public interface INavPage                       // all hooks are optional
{
    Task OnEnterAsync(NavContext c) => Task.CompletedTask;          // after the motion
    Task<bool> OnLeaveAsync(NavContext c) => Task.FromResult(true); // false cancels the move
}

public sealed class Navigator                   // scoped, one for each circuit
{
    NavState State { get; }
    IReadOnlyList<PushEntry> Stack { get; }
    string UrlOf<TPage>();
    void Go<TPage>(IReadOnlyDictionary<string, object?>? query = null);
    Task<TResult?> PushAsync<TPage, TResult>(IDictionary<string, object>? parameters = null);
    Task Close(object? result);
    Task Back();                                // pop the stack, or the previous visited URL
}

// Program.cs: the page table, the only place with URL strings.
builder.Services.AddNav(pages => pages.Map<Home>("/").Map<Counter>("/counter"));
```

The order of one URL move:

```
click / Go / Back
  -> Navigator.Changing   (LocationChanging handler)
       -> active page OnLeaveAsync -> false: PreventNavigation, stop
  -> Navigator.Changed    (LocationChanged)
       -> new NavState, visited list, stack cleared
       -> SiteHost.PlayAsync(move): class on, render, Task.Delay(300), class off
       -> new page OnEnterAsync
```

## Flows

The server check is done: build, prerender and not-found. The browser check of the flows is not done yet.

| Flow | Result | Trace lines (expected from the code) |
|---|---|---|
| F1 | not tested in a browser | `Navigator: LocationChanging Tab -> /counter`, `Home: OnLeaveAsync(...)`, `Navigator: LocationChanged Tab -> /counter`, `SiteHost: play tab`, `Counter: OnEnterAsync(...)` |
| F2 | not tested in a browser | `Navigator: Go<Counter>`, then the F1 lines with `Push` and `play push` |
| F3 | not tested in a browser | `Navigator: Back -> /`, `Counter: OnLeaveAsync(Back ...)`, `LocationChanged Back -> /`, `play back`, `Home: OnEnterAsync(...)` |
| F4 | not tested in a browser | `count` shows 3 |
| F5 | not tested in a browser | `note` keeps "abc": no `Counter: Dispose` line |
| F6 | not tested in a browser | `Navigator: PushAsync<Picker, String>`, `Home: OnLeaveAsync(Push ...)`, `play push`, `Picker: OnEnterAsync`, `Navigator: Close(Red)`, `Navigator: PushAsync result Red`, `Home: picked Red`, `play back`, `Home: OnEnterAsync(Back Picker -> Home)` |
| F7 | not tested in a browser | `LocationChanging Tab -> /`, `Counter: OnLeaveAsync(...)`, bar `guard` shows; `guard-stay` gives `Counter: guard stay`, `Navigator: PreventNavigation`; `guard-leave` gives `Counter: guard leave` and the move |

Server check (curl, port 5108):

| URL | HTTP | `data-test` values in the prerender |
|---|---|---|
| `/` | 200 | `page-home`, `add`, `go-counter`, `pick`, `picked`, `trace`, `tab-home`, `tab-counter` |
| `/counter` | 200 | `page-counter`, `back`, `count`, `note`, `trace`, `tab-home`, `tab-counter` |
| `/nothing` | 200 | `not-found`, `trace`, `tab-home`, `tab-counter` |

## What the default Router gave that we had to write again

- The server endpoint for each URL. With no `@page`, `MapRazorComponents` maps no route. `MapFallback(() => new RazorComponentResult<App>())` renders `App` for each URL.
- The link interception: `EnableNavigationInterceptionAsync` in the first `OnAfterRenderAsync`.
- The URL parse: base-relative path, query, page lookup, not-found.
- The active class of the app bar links (`NavLink` needs a route match; we compute `on` from `NavState`).
- The layout. `SiteHost` is the layout and the router in one component.

## What was hard

- The move kind. `NavigationManager` does not tell push or back. The Navigator sets a pending kind before `NavigateTo`. With no pending kind, `IsNavigationIntercepted` tells a link click (Tab) from the browser button (Back).
- The page instance for the hooks. `DynamicComponent.Instance` with `@ref` into a dictionary (slot -> component) gives the live page.
- A kept, hidden page does not render again by itself. The count stays current only because the Counter takes the `NavState` cascade. A page with no cascade parameter shows old shared state.
- The `TaskCompletionSource` must use `RunContinuationsAsynchronously`. Otherwise the caller continues inside `Close` before the back motion starts.

## Problems

- The flows are not tested in a browser yet. The parent session does that check.
- The not-found URL gives HTTP 200, not 404. The fallback does not know the page table.
- If a second move starts while the guard bar waits, the first `LocationChangingContext` is cancelled, but the bar stays open. `NavContext` does not carry the cancellation token.
- Our visited list and the browser history can differ: `Back()` calls `NavigateTo`, so the browser gets a new entry, not a pop.
- Two fast moves can overlap in `PlayAsync`. There is no queue.
- A push does not change the URL, so a browser refresh loses the Picker. A URL move closes all pushed pages with a `null` result.

## Browser check (2026-11-22, flow script, built-in browser)

| Flow | Result | Note |
|---|---|---|
| F1 | pass | Class `tab` on the new slot, both ways |
| F2 | pass | Class `push` |
| F3 | pass | The guard bar shows first (the note has "abc"). After `guard-leave`: class `back`, path `/` |
| F4 | pass | `count` = 3 on the first run, because Counter takes the `NavState` cascade |
| F5 | pass | "abc" kept |
| F6 | pass | Picker shows over Home with `push`; `pick-red` closes it with `back`; `picked` = "Red"; the URL stays `/` |
| F7 | pass | `guard-stay` keeps `/counter`; `guard-leave` goes to `/` |

The motion was not checked visually: the browser pane was hidden.
