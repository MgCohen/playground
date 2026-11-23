# Ant Design Blazor 1.6.2 (`ReuseTabs`)

| Item | Value |
|---|---|
| Package | `AntDesign` 1.6.2 (latest stable 1.x), with `VersionOverride` in the csproj |
| Setup | `Program.cs` (`AddAntDesign()`), `App.razor` (CSS and JS), `Routes.razor` (`CascadingValue` of `routeData`), `_Imports.razor` (`@using global::AntDesign`), `MainLayout.razor` (`<ReuseTabs>` in place of `@Body`), pages (`[ReuseTabsPage]`, `IReuseTabsPage`) |
| Lines of spike code | 136 (all `.razor` files and `Program.cs`, no empty lines; `01-native` gives 105 with the same count) |

| Capability | Result | How |
|---|---|---|
| Swap pages | yes | `ReuseTabs` puts each visited URL in a `TabPane`. The inactive pane gets `ant-tabs-tabpane-hidden` (`display: none`) |
| Transition | no | `Animated="true"` is set, but in 1.6.2 it only adds the class `ant-tabs-content-animated`. The CSS has no transition for the pane content. Plain swap |
| Direction (push/back) | no | `ReuseTabs` does not know push or back. It knows only the active key (the URL) |
| Lifecycle hooks | part | Tabs events: `OnChange`, `ActiveKeyChanged`, `AfterTabCreated`, `OnTabClick`, `OnClose`. Page interface: `IReuseTabsPage.GetPageTitle` (called once, at the reference capture). NavigationManager: `RegisterLocationChangingHandler`, `LocationChanged`. No activate or deactivate hook on the page |
| Cancel a leave | part | `Tabs.OnEdit` can stop a tab close only. A leave by navigation needs the native `PreventNavigation()` |
| Keep-alive (F5) | yes (from code) | `TabPane` wraps the page in the internal `KeepAlive` component and only hides it. `[ReuseTabsPage(KeepAlive = true)]` is the default; 1.6.2 stores the flag but no render path reads it |
| Typed back result | no | No back API. The spike calls JS `history.back` |
| Shared state (F4) | yes | `CounterState`, a scoped service. The library has no store |

## Flows
| Flow | Result | Trace lines |
|---|---|---|
| F1 | browser check pending | |
| F2 | browser check pending | |
| F3 | browser check pending | |
| F4 | browser check pending | |
| F5 | browser check pending | |

Expected from the code: on a return to a visited page, the trace shows `ReuseTabs: ActiveKeyChanged`, `ReuseTabs: OnChange` and the Nav lines, but no `OnInitialized` and no `Dispose`. The cached page is not created again.

Prerender trace (curl of `/counter`, server console):

```
ReuseTabs: ActiveKeyChanged -> /counter
ReuseTabs: OnChange -> /counter
ReuseTabs: AfterTabCreated -> /counter
Counter: OnInitialized
Counter: IReuseTabsPage.GetPageTitle
ReuseTabs: AfterTabCreated -> /counter
Counter: Dispose
```

## Choices
- **Tab strip:** `TabBarStyle="display:none"`, a plain `Tabs` parameter. Our app bar (`tab-home`, `tab-counter`) does F1. `RenderTabBar` exists in 1.6.2 but the markup does not use it.
- **One component:** `<ReuseTabs>` renders the pages itself. `HidePages` with `<ReusePages>` is the other documented way; it renders all pages with `display:none` too, and adds `ActivedUriChanged`. Not used, to keep the code small.
- **Route data:** `Routes.razor` wraps `RouteView` in `<CascadingValue Value="routeData">`, as the docs tell for `ReuseTabsPageAttribute` and `IReuseTabsPage`. Then the `Body` parameter is not necessary.

## What Ant Design adds
- Service: `builder.Services.AddAntDesign()`. It adds `ReuseTabsService`, `MenuService` and the other Ant Design services.
- CSS: `_content/AntDesign/css/ant-design-blazor.css` (690 KB, the full theme). It can change the look of our plain markup (base styles for `body`, `button`, `h1`).
- JS: `_content/AntDesign/js/ant-design-blazor.js` (97 KB), plus the auto-loaded `AntDesign.*.lib.module.js`.

## Problems
- `dotnet add package AntDesign` wrote a `PackageVersion` line into the root `Directory.Packages.props` (central package management). Fix: revert that file, and use `VersionOverride="1.6.2"` in the csproj, as `03-brouter` does.
- `@using AntDesign` did not compile (`CS0246` for `ReuseTabsPage`, `IReuseTabsPage`). Cause: in the namespace `Spike.AntDesign.Components.Pages`, the name `AntDesign` resolves to `Spike.AntDesign`. Fix: `@using global::AntDesign`.
- `AntDesign` has a `Layout` component. To prevent a name conflict, `Routes.razor` uses the full name `Spike.AntDesign.Components.Layout.MainLayout` (a precaution, not a seen error).
- Run check: `/` and `/counter` return 200, `spike.css` returns 1469 bytes, the Ant Design CSS and JS return 200. No exception in the console.

## Browser check (2026-11-21, built-in browser, pane hidden)

The pane was not on screen, so the page drew 0 frames. The functional flows ran; the motion is not checked.

| Flow | Result |
|---|---|
| F1 | pass. Both pages stay in the DOM; the hidden one has no box |
| F2 | pass |
| F3 | pass |
| F4 | fail: count 0. The kept Counter page does not render again, and ReuseTabs gives the page no activation hook. A fix needs a subscription to `CounterState.Changed` in the page |
| F5 | pass: note "abc" kept |
