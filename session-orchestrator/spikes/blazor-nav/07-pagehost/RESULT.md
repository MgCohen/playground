# Our shape: PageHost (no library)

| Item | Value |
|---|---|
| Package | None. Default Blazor `Router` (.NET 10) |
| Setup | `Routes.razor` (`<Found>` renders `PageHost`), `Navigation/Navigator.cs`, `Navigation/PageHost.razor` and `.razor.css`, `Program.cs` (`AddScoped<Navigator>`), `App.razor` (link to `Spike.PageHostNav.styles.css`), `MainLayout.razor` (plain frame component), 3 pages |
| Lines of spike code | 346, no empty lines, no csproj. The reusable part is 213 (`Navigator.cs` 152, `PageHost.razor` 53, CSS 8). App and pages are 133 |

| Capability | Result | How |
|---|---|---|
| Swap pages | yes | `Router` finds the page. `PageHost` renders it with `DynamicComponent` (`OpenComponent` plus the route values) in a `slot` wrapper |
| Transition | yes | `PageHost` adds the class `push`, `back` or `tab` to the slot, renders, waits 300 ms (`Task.Delay`), then removes the class. Scoped CSS has the keyframes. No JavaScript file |
| Direction (push/back) | yes | `Navigator` decides the kind: `Push(url)` and `PushAsync` are Push, `BackAsync` and a popstate to the previous visited URL are Back, a link click is Tab |
| Lifecycle hooks | yes | `INavPage.OnLeaveAsync(NavContext)` before the move, `INavPage.OnEnterAsync(NavContext)` after the motion. Both are default interface methods, so each one is optional |
| Cancel a leave | yes | `RegisterLocationChangingHandler` calls `OnLeaveAsync` on the top page. If it returns false, the handler calls `PreventNavigation()`. Push and pop of pushed pages use the same hook |
| Keep-alive (F5) | yes | One slot for each page type, `@key` on the type, `hidden` on the inactive slots. The host does not dispose the page |
| Typed back result | yes | `PushAsync<Picker, string>()` awaits a `TaskCompletionSource`. The Picker calls `CloseAsync("Red")` |
| Shared state (F4) | yes | `CounterState`. A kept page renders again on each host render, because `DynamicComponent` sets its parameters again |

## API

```csharp
public enum NavKind { Push, Back, Tab }
public sealed record NavContext(NavKind Kind, string From, string To, CancellationToken Cancel = default);

public interface INavPage
{
    Task OnEnterAsync(NavContext c) => Task.CompletedTask;            // after the motion
    Task<bool> OnLeaveAsync(NavContext c) => Task.FromResult(true);   // false cancels the move
}

public sealed class Navigator   // scoped: one for each circuit
{
    public IReadOnlyList<PageSlot> Stack { get; }   // pushed pages, the last one is on top
    public string Current { get; }                  // "Picker" or "/counter"
    public void Push(string url);                   // URL page, kind Push
    public Task<TResult?> PushAsync<TPage, TResult>(IDictionary<string, object>? parameters = null) where TPage : IComponent;
    public Task CloseAsync(object? result);         // closes the top pushed page, gives the result
    public Task BackAsync();                        // pops a pushed page, else history.back
}
```

Order of one move: `OnLeaveAsync` (awaited), the URL changes or the stack changes, the host shows the page with the class, 300 ms, `OnEnterAsync`.

## Flows
| Flow | Result | Trace lines (expected) |
|---|---|---|
| F1 | browser check pending | `Navigator: LocationChanging Tab / -> /counter`, `Home: OnLeaveAsync Tab`, `PageHost: show /counter (tab)`, `Counter: OnEnterAsync Tab` |
| F2 | browser check pending | `Navigator: Push(counter)`, `LocationChanging Push / -> /counter`, `Home: OnLeaveAsync Push`, `PageHost: show /counter (push)`, `Counter: OnEnterAsync Push` |
| F3 | browser check pending | `Navigator: BackAsync history.back`, `LocationChanging Back /counter -> /`, `Counter: OnLeaveAsync Back`, `PageHost: show / (back)`, `Home: OnEnterAsync Back` |
| F4 | browser check pending | `count` shows 3 |
| F5 | browser check pending | "abc" kept. The guard bar shows on the leave: click `guard-leave` (see Problems) |
| F6 | browser check pending | `Navigator: PushAsync<Picker>`, `Home: OnLeaveAsync Push`, `PageHost: show Picker (push)`, `Picker: OnEnterAsync Push`, `Navigator: CloseAsync(Red)`, `PageHost: show / (back)`, `PushAsync<Picker> result Red`, `Home: picked Red`, `Home: OnEnterAsync Back` |
| F7 | browser check pending | `LocationChanging Tab /counter -> /`, `Counter: OnLeaveAsync Tab`, bar `guard`. `guard-stay`: `Counter: guard stay`, `Navigator: PreventNavigation`. `guard-leave`: `Counter: guard leave`, `PageHost: show / (tab)` |

Run check (port 5107): `/` 200 and `/counter` 200 with all `data-test` IDs of the page, the tabs and `trace`. `spike.css` 200 (1469 bytes), `Spike.PageHostNav.styles.css` 200 (653 bytes). `dotnet build`: 0 warnings, 0 errors.

## Problems
- The project name `Spike.PageHost` gives a namespace with the same name as the `PageHost` class. Then the name `PageHost` resolves to the namespace. Fix: the project is `Spike.PageHostNav`.
- With no `RouteView`, the layout does not apply. Fix: `MainLayout` is a plain component around the `Router` (as in `03-brouter`). Then the kept pages do not keep a copy of the frame.
- F5 and F7 use the same page. If `note` is not empty, each leave of Counter shows the guard bar, also in F5. Do F5 with `guard-leave`.
- `note` uses `@bind:event="oninput"`. With the default `onchange`, the value can come after the leave hook, and then the guard does not see "abc".
- `BackAsync` with an empty stack calls `history.back` through `IJSRuntime` (no JavaScript file). If Counter is the first page of the tab, this leaves the app.
- A URL move (for example a tab click) while the Picker is open closes the Picker. The caller gets `null`.
- The prerender does not play the motion and does not call the hooks (`RendererInfo.IsInteractive` is false). The first interactive render plays a Tab move with `From` = "".

## What was hard
- The kind of a popstate. `LocationChangingContext` does not tell back from forward. Our list of visited URLs gives Back when the target is the previous URL. A link click has `IsNavigationIntercepted` = true, so a tab to the previous URL stays Tab.
- The page instance for the hooks. `@ref` on `DynamicComponent` gives `Instance`. The ref is set only after the first render, so `OnEnterAsync` runs after the 300 ms wait.
- The thread of the hooks. `PlayAsync` and the guard bar use `InvokeAsync`, because the location changing handler is not a UI event.
- Exceptions in the handler end the circuit. The handler catches all exceptions and writes them to the trace.

## Browser check (2026-11-22, flow script, built-in browser)

| Flow | Result | Note |
|---|---|---|
| F1 | pass | Class `tab` on the new slot, both ways |
| F2 | pass | Class `push` |
| F3 | pass | The guard bar shows first (the note has "abc"). After `guard-leave`: class `back`, path `/` |
| F4 | pass after a fix | Run 1 showed `count` = 0: a kept page does not render again by itself. Fix: Counter subscribes to `CounterState.Changed`. Run 2 showed 3 |
| F5 | pass | "abc" kept |
| F6 | pass | Picker shows over Home with `push`; `pick-red` closes it with `back`; `picked` = "Red"; the URL stays `/` |
| F7 | pass | `guard-stay` keeps `/counter`; `guard-leave` goes to `/` |

The motion was not checked visually: the browser pane was hidden.
