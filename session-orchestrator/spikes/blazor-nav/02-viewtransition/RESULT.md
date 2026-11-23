# Toolbelt.Blazor.ViewTransition 2.0.0

| Item | Value |
|---|---|
| Package | `Toolbelt.Blazor.ViewTransition` 2.0.0 (latest stable) |
| Setup | csproj (package, central versions off), `Program.cs` (`AddViewTransition()`, `NavMark`), `Routes.razor` (`ViewTransitionRouter`), `App.razor` (CSS link), `NavMark.cs`, `wwwroot/viewtransition.css`, one line in each page |
| Lines of spike code | 130 (pages, layout, routes, program, `NavMark.cs`, CSS; no empty lines). The same files in `01-native` have 85 |

| Capability | Result | How |
|---|---|---|
| Swap pages | yes | `ViewTransitionRouter` wraps the Blazor `Router` (a `DynamicComponent`; `TypeOfRouter` can change it) |
| Transition | yes | The router calls `IViewTransition.BeginAsync()` in `OnNavigateAsync` and `EndAsync()` in its `OnAfterRenderAsync`. The browser runs `document.startViewTransition`. The motion is CSS only: `::view-transition-old/new(stage)`, 300 ms |
| Direction (push/back) | part | The library does not know push or back. The page sets `NavMark.Next` ("push" or "back") before it navigates. `OnNavigateAsync` writes it to `<html data-nav>` with one JS interop call (`document.documentElement.setAttribute`). A tab click or the browser back button gets "fade" |
| Lifecycle hooks | part | One hook only: `ViewTransitionRouter.OnNavigateAsync` (before the swap). No event for the start or the end of the transition. The NavigationManager hooks and the `TracedPage` hooks stay as in `01-native` |
| Cancel a leave | no | The library adds nothing. The Blazor `RegisterLocationChangingHandler` and `<NavigationLock>` still work (not used) |
| Keep-alive (F5) | no | The router disposes the old page, as in `01-native` |
| Typed back result | no | No back method. The spike calls JS `history.back` |
| Shared state (F4) | yes | `CounterState`, a scoped service (the library has no state feature) |

## Flows
| Flow | Result | Trace lines |
|---|---|---|
| F1 | browser check pending | |
| F2 | browser check pending | |
| F3 | browser check pending | |
| F4 | browser check pending | |
| F5 | browser check pending | |

Expected trace order: `LocationChanging`, `ViewTransitionRouter: OnNavigateAsync -> /<path> (<mark>)`, new page `OnInitialized`, old page `Dispose`, `LocationChanged`. The library does not call `BeginAsync()` on the first navigation.

Run check (no browser): `/` and `/counter` return 200 with all `data-test` values. `spike.css` (1469 bytes), `viewtransition.css` and the library `script.min.js` return 200. The console has no exception. The prerender log shows `ViewTransitionRouter: OnNavigateAsync -> / (fade)`.

## Problems
- `dotnet add package` wrote the version into `Directory.Packages.props` of the repository (central package versions). That file is outside the spike folder. Fix: I reverted that file. The csproj sets `ManagePackageVersionsCentrally` to `false` and has `Version="2.0.0"`.
- Build warning RZ2012: `ViewTransitionRouter` marks `NotFound` as required. The spike has no not-found page, so I kept the warning.
- `OnNavigateAsync` also runs in the prerender, where JS interop is not available. An exception there can stop the render. Fix: the handler skips the JS call when `RendererInfo.IsInteractive` is false.
- The whole document is the default transition element (`root`), so the trace and the app bar would slide too. Fix: `.stage` gets `view-transition-name: stage`, and only the stage slides. The rest uses the default fade. `::view-transition-group(stage)` gets `overflow: clip`, so the slide stays in the stage.
- The View Transitions API needs a browser with `document.startViewTransition`. Without it, the library swaps the page with no motion.

## Browser check (2026-11-21, built-in browser, pane hidden)

The pane was not on screen, so the page drew 0 frames. The functional flows ran; the motion is not checked.

| Flow | Result |
|---|---|
| F1 | pass: `/counter`, then `/` |
| F2 | pass: `/counter`. `startViewTransition` is called (checked with a wrapper) |
| F3 | pass: `/` |
| F4 | pass: count 3 |
| F5 | fail (expected): note empty |

Finding: the trace shows `OnNavigateAsync -> /counter (back)` on the back to `/`: check whether the logged path is the old or the new page.
