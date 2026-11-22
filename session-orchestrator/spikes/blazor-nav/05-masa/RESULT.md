# MASA Blazor 1.11.9 (page stack)

| Item | Value |
|---|---|
| Package | `Masa.Blazor` 1.11.9 and `Masa.Blazor.MobileComponents` 1.11.9 (latest stable; 1.12.0 is alpha) |
| Setup | csproj (2 packages, no central versions), `Program.cs` (`AddMasaBlazor().AddMobileComponents()`), `App.razor` (MASA CSS and JS), `_Imports.razor` (`global::Masa.*`), `MainLayout` (`MApp`, `PPageStack`, `PPageStackTab`), both pages |
| Lines of spike code | 147 (pages, layout, routes, program; no empty lines). Reference: 85 |

## The model

MASA has two kinds of pages. The spike uses both.

```
/          tab page    (TabRule "^/$")         kept in the DOM, hidden with d-none
/counter   tab page    (TabRule "^/counter$")  kept in the DOM, hidden with d-none
/counter/stack  stack page (Push)              fullscreen MDialog on top of the tabs, removed on back
```

- The app bar links use `PPageStackTab`. A click does `NavigationManager.Replace`. There is no motion.
- `go-counter` does `NavController.Push("/counter/stack")`. `Counter.razor` has two routes, `/counter` and `/counter/stack`.
- A push to `/counter` is not possible: that path matches a tab rule, so the tab container also shows it.

## Capabilities

| Capability | Result | How |
|---|---|---|
| Swap pages | yes | Tabs: `PPageStackContainer` hides the old tab with `d-none`. Stack: `PPageStack` puts each pushed page in an `MDialog` |
| Transition | part | Stack pages only: `dialog-right-transition`, 300 ms, plus an underlay slide of the tab below. A tab change has no motion (no fade) |
| Direction (push/back) | yes | Push slides in from the right. Back: `CloseTopPages` sets `Stacked=false`, and the same transition slides the page out to the right (the reverse way). Browser check must confirm |
| Lifecycle hooks | part | `PStackPageBase`: `OnPageActivated(object? state)`, `OnPageDeactivated` (stack pages only). Controller events: `StackPush`, `StackPop`, `PageClosed`, `TabChanged`, `TabRefreshRequested`. Tab pages get no page hooks |
| Cancel a leave | no | No API in the page stack. Only the native `NavigationLock` |
| Keep-alive (F5) | part | Tabs: yes, the Counter tab instance stays and keeps `note`. Stack: no, back disposes the stack page |
| Typed back result | part | `GoBack(1, state)` sends `object?`. Only a stack page below receives it in `OnPageActivated(state)`. Home is a tab, so it reads `PageStackPopEventArgs.State` from the `StackPop` event (type `String`, a cast is necessary) |
| Shared state (F4) | yes | `CounterState` (scoped). MASA has no state feature of its own |

## Flows

| Flow | Result | Trace lines |
|---|---|---|
| F1 | browser check pending | Expected: `Nav: TabChanged -> /counter`, `Counter: OnInitialized` (first visit only), then `Nav: TabChanged -> /` |
| F2 | browser check pending | Expected: `Home: Push(/counter/stack)`, `Nav: StackPush -> /counter/stack`, `Counter: OnInitialized`, `Counter: OnPageActivated(state=null)` |
| F3 | browser check pending | Expected: `Counter: GoBack(1, "...")`, `Nav: StackPop delta=1 state=...`, `Home: back result String "..."`, a second `StackPop` with `state=null` (from popstate), `Counter: Dispose`, `Nav: PageClosed /counter/stack` |
| F4 | browser check pending | Expected: `count` shows 3 |
| F5 | browser check pending | Expected: through the tabs, "abc" stays. Through push and back, "abc" is lost |

## What MASA adds

| Item | Size |
|---|---|
| Packages | `Masa.Blazor`, `Masa.Blazor.MobileComponents` (220 KB dll), and 6 more: `BemIt`, `DeepCloner.Core`, `FluentValidation` (2), `OneOf` (2), `Util.Reflection` |
| Root component | `MApp`, necessary for `MDialog` |
| CSS | `masa-blazor.min.css`, 582 KB (full Material theme and reset) |
| JS | `masa-blazor.js`, 37 KB, plus ES modules (173 KB for all `js/`); the page stack imports `js/components/page-stack` at the first render |

## Problems

- The repository uses central package versions (`NU1008`). Fix: `ManagePackageVersionsCentrally=false` in the spike csproj.
- The namespace `Spike.Masa` hides the root namespace `Masa`: `@using Masa.Blazor` fails with `CS0234`. Fix: `@using global::Masa.Blazor`.
- A stack page is a fullscreen fixed `MDialog`. It covers the full window, also the trace panel and the app bar. Fix: `MApp` is only inside `.stage`, and `.stage` has `style="transform: translateZ(0)"`. Then the fixed dialog stays inside the stage. Browser check must confirm.
- `PStackPageBase` and `TracedPage` are both base classes. Fix: `Counter` inherits `PStackPageBase` and writes the `OnInitialized`, `OnAfterRender(first)` and `Dispose` lines itself.
- Tab pages are outside the cascading `PPageStack`, so `OnPageActivated` does not run for Home. Fix: Home uses the `StackPop` event for the back result.
- `StackPop` fires two times for one `GoBack`: once with the state, once from popstate with `state=null`. Not fixed; recorded.
- `GoBack` on the Counter tab (no stack page) only calls `history.go(-1)`. Fix: if the page did not get `OnPageActivated`, `back` does `NavigateTo("/", replace: true)`.
- A server prerender of `/counter/stack` shows only the loader: `PPageStackItem` waits 300 ms before it renders the page.
- The MASA reset can change the look of buttons and fonts. Fix: the MASA CSS loads before `spike.css`. Browser check must confirm the look.
- If the Counter tab was visited and a push then opens `/counter/stack`, the DOM has two `page-counter` elements (one hidden with `d-none`). A test must select the visible one.

## Browser check (2026-11-21, built-in browser, pane hidden)

The pane was not on screen, so the page drew 0 frames. The functional flows ran; the motion is not checked.

| Flow | Result |
|---|---|
| F1 | pass: `/counter`, then `/` |
| F2 | pass: `/counter/stack`. Home stays visible under the pushed page |
| F3 | pass: `/`. Home gets the back result "abc" from `StackPop`, then a second `StackPop` with null |
| F4 | pass: count 3 |
| F5 | pass for the tab page (note kept). The pushed page loses its note (`PageClosed`, `Counter: Dispose`) |
