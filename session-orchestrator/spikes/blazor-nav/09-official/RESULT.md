# 09 official: the Microsoft mechanisms, measured (.NET 10.0.403 SDK)

| Item | Value |
|---|---|
| Package | None. Official parts only |
| Setup | `Lab/EventHandlers.cs` (5 custom events), `wwwroot/Spike.Official.lib.module.js` (JS initializer and the lab helpers), `Lab/LabPage.cs` (`IHandleEvent`), `Lab/Activity.cs` (`CircuitHandler`), 5 pages, `MainLayout` |
| Lines of spike code | 367, no empty lines, without the shared base and the flow scripts |
| Port | 5109 |
| Run | `dotnet run --urls http://localhost:5109`, then `node flows.cjs` and `node flows-guard.cjs` (Playwright 1.64.1, headless Chromium) |

The spike does not follow the F1 to F7 flows of `SPEC.md`. It has one lab page for each group of mechanisms. All times are C# ms from the state change, from one run on 2026-11-22.

## Motion: custom events against the interop wait

Each test renders a new element (`@key`). In the same `OnAfterRenderAsync`, the page starts two waits: the interop wait (`lab.wait`, the `settle` mode of `motion.js`) and the hybrid (`lab.settleAndDispatch`: the same wait, then a DOM event `orch-settled` that Blazor delivers as `@onmotionsettled`).

| Test | CSS | Probe in `OnAfterRender` | `@onmotionend` / `@onmotiontransend` | `@onmotioncancel` | Interop wait | Hybrid event |
|---|---|---|---|---|---|---|
| single (3 runs) | 1 animation, 300 ms | class yes, 1 animation | 1 event at 344, 348, 349 | none | `end` at 344, 347, 348 | `end` at 342, 347, 348 |
| stagger | 3 children, 300 ms, delays 0/100/200 | class yes, 3 animations | 3 events: 347, 430, 548 | none | `end` at 547 | `end` at 547 |
| none | class with no motion | class yes, 0 animations | **no event** | none | `end` at 64 | `end` at 63 |
| cancel | class removed at 113 ms | class yes, 1 animation | no event | 1 event at 133 | `cancel` at 116 | `cancel` at 116 |
| trans | 2 transitions (opacity, transform), 300 ms | class yes, 2 animations | 2 events at 332 (one for each property) | none | `end` at 332 | `end` at 331 |

Findings:

1. The latency is the same. A custom event and the interop wait reach C# in the same ms (344 against 344).
2. A custom event is one event for each animation, each child and each property. C# must know the count to know the end. CSS owns the count, so C# cannot know it. The interop wait gives one end for all.
3. With no motion, no event comes. The wait must have its own timer (lesson I13). The interop wait ends at 64 ms (3 frames).
4. `OnAfterRender` comes after the DOM change: 7 of 7 probes saw the class, and `getAnimations` saw all animations at the first call.
5. The hybrid works. JS owns the wait, and an official custom event delivers the end. It gives no `Task`: the caller cannot await it, and it has no call id and no cancel.

## Input: the official hooks for a trace

| Test | Result |
|---|---|
| `IHandleEvent` on a page base | Sees each event of the page: `MouseEventArgs`, `ChangeEventArgs`, `MotionEventArgs`, and also `LocationChangingContext` (the `NavigationLock` callback). It does not see the element |
| Custom event `tracedclick` (browser event `click`) on the layout root | One handler sees each click of the app with the `data-test` of the element. It runs after the handler of the element |
| `tracedclick` and `@onclick:stopPropagation` | `stopPropagation` stops the parent `onclick`, but not `tracedclick`: it is a different Blazor event type. The trace sees each click |
| `@onclick:stopPropagation="stop"`, the handler flips `stop` | Click 1 propagates, click 2 does not, click 3 does. The flag of the last render applies, not the flag that the handler sets |
| `@onclick:preventDefault="block"` on a checkbox, the handler sets `block` | Click 1 checks the box (`block` was false). Click 2 does not uncheck it. Same rule: the render decides, not the event |
| `CircuitHandler.CreateInboundActivityHandler` | 4 inbound activities for each click. It gives the time of each activity, but not its kind, its element or its handler |

## Navigation: handlers, `NavigationLock`, `HistoryEntryState`

The layout handler stands for `MotionHost`: it awaits 200 ms in `RegisterLocationChangingHandler`. The Guard page has a `<NavigationLock OnBeforeInternalNavigation>` that waits for the guard bar when the note is not empty.

| Test | Trace | Finding |
|---|---|---|
| Tab with the bar | `Layout handler: start` and `NavigationLock start` in the same ms; `Layout handler: end` 203 ms later, the bar still open | **The handlers run at the same time.** A motion handler does its work before the guard decides |
| Empty note, push | Page change 3 ms after `Layout handler: end` | The navigation waits for all handlers |
| Second move while the bar waits | `Guard: token canceled for /`, then the new handlers start in the same ms | The token of the old move fires at once. The old `PreventNavigation` has no effect |
| Browser back with the bar, `guard-stay` | URL stays `/guard`, `history.length` stays 3 | `PreventNavigation` works for a popstate, with no extra history entry |
| Browser back, `guard-leave` | Page change 15 ms after the click | |
| `NavigateTo(url, new NavigationOptions { HistoryEntryState = "depth=2" })` | `LocationChangingContext.HistoryEntryState` = `depth=2`, `Nav.HistoryEntryState` = `depth=1` (the current entry) | The handler sees the state of both entries |
| Browser back and forward | Back: target `depth=1`, current `depth=2`. Forward: target `depth=2`, current `depth=1` | **The direction comes from the state.** Spike 07 needed a list of visited URLs for this |
| `NavLink` click | State `null` | A link cannot set a state. Only a move from code can |
| `IsNavigationIntercepted` on a popstate | `False` in `LocationChanging`, `True` in `LocationChanged` | The two events do not agree. Use only the value of `LocationChanging` |
| Enhanced navigation events (`enhancedload`, `enhancednavigationstart`, `enhancednavigationend`) | 0, 0 and 0 after 12 moves | They fire only for static SSR with enhanced navigation. An interactive app gets none |

## Problems

- Run 1 showed no bar for the second move. Cause: a spike bug. The old, canceled wait set the shared `bar` field to `null` after the new wait set it. Fix: each wait has its own `TaskCompletionSource`, and only the newest one clears the field. A real guard needs the same rule.
- `ConfirmExternalNavigation` was not tested. It shows the browser `beforeunload` dialog, and the user rule says "no JS `confirm`".
- The runs use headless Chromium in Playwright, not the WebView2 of the tray. The times are from one machine and one run.
