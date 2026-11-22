# 10 events: an event channel in place of IJSRuntime calls (.NET 10.0.403 SDK)

| Item | Value |
|---|---|
| Question | Can official custom events carry the navigation and motion pipeline, with no `IJSRuntime` call? |
| Package | None |
| Setup | `Lab/Channel.cs` (commands and phases), `Components/Outlet.razor` (renders the open commands), `Lab/EventHandlers.cs` (one custom event `onorchdone`), `wwwroot/Spike.Events.lib.module.js`, one navigation handler in `MainLayout`, 2 pages |
| Lines of spike code | 382, no empty lines, without the shared base and the flow script. The channel itself: `Channel.cs` 57, `Outlet.razor` 15, the command part of the script about 30 |
| Port | 5110 |
| Run | `dotnet run --urls http://localhost:5110`, then `node flows.cjs` (Playwright 1.64.1, headless Chromium) |

## The two transports

```
Event channel (no IJSRuntime)                      IJSRuntime (as the product today)
C#  Channel.Send("vt", "push")                     C#  await JS.InvokeVoidAsync("lab.begin", "push")
 -> Outlet renders <i data-orch-req=7 ...>          -> one interop message
JS  MutationObserver sees the element              JS  lab.begin runs, the Promise resolves
 -> runs the verb, dispatches "orch-done"          C#  the call returns
    {id 7, phase "captured"}, later "finished"     C#  await JS.InvokeAsync("lab.finished")  (a second call)
C#  @onorchdone -> Channel.Complete -> Task ends
```

Each run of the pipeline is the same in both modes: one `RegisterLocationChangingHandler` gets the direction from `HistoryEntryState`, starts a view transition, waits for "captured", and lets the router render. Then the new page waits for the settle of its blocks.

## Results (20 navigations and 10 enter waits in each mode, one run on 2026-11-22)

Values are min / median / max, in C# ms from the start of the navigation.

| Step | Event channel | IJSRuntime |
|---|---|---|
| Captured (the handler can return) | 15 / 24 / 33 | 16 / 24 / 33 |
| New page rendered | 33 / 34 / 44 | 33 / 34 / 35 |
| View transition finished | 332 / 349 / 366 | 332 / 349 / 368 |
| Enter wait of the blocks ended | 513 / 514 / 515 | 513 / 514 / 515 |
| Inbound circuit activities from the start to "finished" | 12 / 14 / 14 | 10 / 11 / 12 |
| A JS error in the command | `TimeoutException` after the C# limit (2007 ms) | `JSException "lab error"` after 14 ms |

Findings:

1. The event channel works. It carries a two-phase command ("captured", then "finished") with one command. The interop mode needs two calls for the same two phases.
2. The times are the same. The transport is not the cost. The view transition and the CSS motion are the cost.
3. The channel costs 2 to 3 more inbound activities for each navigation: the outlet renders one time to add the command and one time to remove it.
4. The channel has no error path. A JS error gives no event, so only a C# time limit ends the wait. `IJSRuntime` gives the error with its message in 14 ms.
5. The channel is our own small RPC: ids, phases, an outlet, an observer. `IJSRuntime` gives the ids, the errors, the cancellation of the C# wait and typed results for free.
6. A render in a location changing handler works: the outlet rendered the command while the handler waited, and the router waited for the handler.

## Problems

- `TracedPage` logged `OnAfterRender(first)` two times on Deep in run 1. Cause: a spike bug, the page called `base.OnAfterRender` in `OnAfterRenderAsync`. Fixed before run 2.
- The channel works only while the outlet is in the DOM and the circuit is interactive. A command before the first interactive render has no outlet. This was not tested.
- Headless Chromium only. The tray host (WebView2 with our own `WebViewManager`) was not tested.
