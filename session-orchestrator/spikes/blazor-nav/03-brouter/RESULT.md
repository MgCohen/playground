# Bit.Brouter 10.7.0

| Item | Value |
|---|---|
| Package | `Bit.Brouter` 10.7.0 (latest stable 10.x; 11.0.0 is prerelease only) |
| Setup | csproj (`PackageReference` with `VersionOverride`), `Program.cs` (`AddBitBrouterServices`), `Routes.razor` (`<Brouter>`, `<Broute>`), `MainLayout.razor` (frame component, `BrouterLink`, global hooks), both pages (`IBrouterRoute`), `App.razor` (`brouter.css`), `wwwroot/brouter.css` |
| Lines of spike code | 159 (143 C# and Razor, 16 CSS; no empty lines, no csproj) |

| Capability | Result | How |
|---|---|---|
| Swap pages | yes | `<Brouter>` with `<Broute Path Component>`; `IBrouter.Navigate`, `BrouterLink` |
| Transition | yes | `o.ViewTransitions = true` wraps each navigation in `document.startViewTransition`. Default animations are off (`ViewTransitionDefaultAnimations = false`); `brouter.css` animates the `stage` group only, 300 ms |
| Direction (push/back) | yes | Brouter sets `data-brouter-nav="push\|pop\|replace"` on `<html>`. CSS: push in from the right, pop in from the left, replace (tabs) fades. `NavigationType` is also on the hook context |
| Lifecycle hooks | yes | Page (`IBrouterRoute`): `OnActivated`, `OnDeactivated`, `OnDeactivating`, `OnRenavigating`, `OnRenavigated` (all `...Async`). Global (`IBrouter`): `OnNavigating`, `OnNavigated` (also `OnError`, not used) |
| Cancel a leave | yes | `OnDeactivatingAsync` calls `context.Cancel()`. Preventive: the URL does not change. Spike: Counter cancels the first leave if `note` is "stay" (also `LeaveGuard` on `Broute`, not used) |
| Keep-alive (F5) | yes | `<Broute KeepAlive="true">` on `/counter`. The page stays mounted and hidden; `OnDeactivated` reason `Hidden` |
| Typed back result | no | `IBrouter.Back()` and `BackAsync(int)` have no result value. Only a history state string (`Navigate(url, historyState:)`) |
| Shared state (F4) | yes | `CounterState` (scoped service). Brouter has no store; loaders are for route data |

## Flows
| Flow | Result | Trace lines |
|---|---|---|
| F1 | browser check pending | |
| F2 | browser check pending | |
| F3 | browser check pending | |
| F4 | browser check pending | |
| F5 | browser check pending | |

Expected for the browser check:
- Tabs are `BrouterLink Replace="true"`. Thus a tab change is a replace and fades, and it does not add a history entry.
- `Back` calls `IBrouter.Back()`. Brouter marks it as `Pop`, so the page comes in from the left.
- Cancel test: type "stay" in `note`, leave the field, click `tab-home`. The trace must show `Counter: Cancel()`, and the page must stay. The second click must leave.

## Problems
- `dotnet restore` failed with NU1008: the repository uses central package management. Fix: `VersionOverride="10.7.0"` on the `PackageReference`, so no file outside the folder changes.
- With `Brouter.DefaultLayout`, the framework `RouteView` puts the layout inside the page subtree. Then a keep-alive page also keeps a copy of the frame. Fix: `MainLayout` is now a plain component with `ChildContent`, and `Routes.razor` puts `<Brouter>` inside it.
- The default animations of Brouter apply to the `root` snapshot. Thus the full frame, the trace and the app bar move. Fix: `.stage` has `view-transition-name: stage`; the `root` animation is off.
- `<Layout.MainLayout>` gave warning RZ10012 (not found as a component). Fix: `@using Spike.Brouter.Components.Layout` in `Routes.razor`.
- No JS tag is necessary. Brouter imports `_content/Bit.Brouter/bit-brouter.js` as a module. A check with `Accept-Encoding: gzip` gives 200 and 2430 bytes (the README warns about an empty compressed file).
- The `@page` directives stay on the pages. `MapRazorComponents` uses them for the server endpoints of `/` and `/counter`. Brouter does not discover them, because `<Brouter>` has no `AppAssembly`.
- In prerender, Brouter runs `OnNavigating` and `OnNavigated` (each GET logs them), but no page hooks.

Run check (port 5103): `/` 200, `/counter` 200, `spike.css` 200 (1469 bytes), `brouter.css` 200, `bit-brouter.js` 200. No exception in the console output.

## Browser check (2026-11-21, built-in browser, pane hidden)

The pane was not on screen, so the page drew 0 frames. The functional flows ran; the motion is not checked.

| Flow | Result |
|---|---|
| F1 | pass. Direction mark `replace` |
| F2 | pass. Direction mark `push` |
| F3 | pass. Direction mark `pop`. Order: `OnDeactivating (Hidden)`, `OnNavigating (Pop)`, about 500 ms, `OnDeactivated (Hidden)`, `Home: OnInitialized`, `OnNavigated`, `Home: OnActivated (first=True)` |
| F4 | first run fail: count 0. Fix: `StateHasChanged()` in `OnActivatedAsync`. Then pass: count 3 |
| F5 | pass: note "abc" kept |
| Cancel | pass: with note "stay", the first leave is cancelled (`OnDeactivating`, `Cancel()`), the second leave goes |

Finding: a kept page does not render again when it shows. If it shows shared state, render it in `OnActivatedAsync` or subscribe to the state.
