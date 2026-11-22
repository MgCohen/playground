# Native Blazor (.NET 10.0.403 SDK)

| Item | Value |
|---|---|
| Package | None. `Router`, `NavLink`, `NavigationManager` |
| Setup | Blazor Web App, interactive server. `MainLayout` registers the two NavigationManager hooks |
| Lines of spike code | 85 (pages, layout, routes, program; no empty lines) |

| Capability | Result | How |
|---|---|---|
| Swap pages | yes | `Router` swaps the page in one render |
| Transition | no | No API. The old page is gone in the same render |
| Direction (push/back) | no | The router does not know push or back |
| Lifecycle hooks | part | `RegisterLocationChangingHandler` (before), `LocationChanged` (after), page `OnInitialized` and `Dispose` |
| Cancel a leave | yes | `LocationChangingContext.PreventNavigation()` or `<NavigationLock>` (not used in the spike) |
| Keep-alive (F5) | no | The router disposes the old page; the note is lost |
| Typed back result | no | No back method. The spike calls JS `history.back` |
| Shared state (F4) | yes | `CounterState`, a scoped service (one for each circuit) |

## Flows
| Flow | Result | Trace lines |
|---|---|---|
| F1 | pass, no motion | `LocationChanging -> /counter`, `Counter: OnInitialized`, `Home: Dispose`, `LocationChanged -> /counter`, `Counter: OnAfterRender(first)` |
| F2 | pass, no motion | `Home: NavigateTo(counter)`, then the same order as F1 |
| F3 | pass, no motion | `Counter: history.back()`, `LocationChanging -> /`, `Home: OnInitialized`, `Counter: Dispose`, `LocationChanged -> /` |
| F4 | pass | `count` shows 3 |
| F5 | fail (expected) | `note` is empty after the return |

The new page is initialized before the old page is disposed, in the same render. The order is: changing, new init, old dispose, changed, new first render.

## Problems
- An exception in a location changing handler ends the circuit. After that, no button works. Cause: for a navigation from code, `context.TargetLocation` is relative (`counter`), and `ToBaseRelativePath` throws. Fix: `Nav.ToBaseRelativePath(Nav.ToAbsoluteUri(context.TargetLocation).ToString())`.
- A run from the repository root served empty static files. Fix: `RunWorkingDirectory` in `Directory.Build.props`, and `builder.WebHost.UseStaticWebAssets()`, because the app runs as Production.
