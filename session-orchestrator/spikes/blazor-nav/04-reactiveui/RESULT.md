# ReactiveUI 27.0.1 (ReactiveUI.Blazor 27.0.1)

| Item | Value |
|---|---|
| Package | `ReactiveUI.Blazor` 27.0.1 (pulls `ReactiveUI` 27.0.1, `ReactiveUI.Binding` 10.0.0, `ReactiveUI.Primitives` 10.0.0) |
| Setup | `Program.cs` (RxAppBuilder, view map, scoped `AppScreen`), `ViewModels.cs`, `ReactiveRouterHost.razor`, `Shell.razor` (the only `@page "/"`), `MainLayout.razor`, `Home.razor`, `Counter.razor` |
| Lines of spike code | 217 (no empty lines). Of these, `ReactiveRouterHost.razor` is 23 lines (the sample host has 56) |

| Capability | Result | How |
|---|---|---|
| Swap pages | yes | `RoutingState.CurrentViewModel` changes; `ReactiveRouterHost` renders the mapped view with `ViewModel` set |
| Transition | no | No motion API in ReactiveUI. Plain swap |
| Direction (push/back) | part | The router knows the kind (`Navigate`, `NavigateBack`, `NavigateAndReset` are separate commands), but nothing uses it for motion |
| Lifecycle hooks | part | View model: `WhenActivated` (activation, and its disposables on deactivation). Router: `Navigate`, `NavigateBack`, `NavigateAndReset` (command results), `CurrentViewModel`. View: Blazor `OnInitialized`, `OnAfterRender(first)`, `Dispose` |
| Cancel a leave | no | `RoutingState` has no leave guard. The only block is the `CanExecute` of `NavigateBack` (stack of one) |
| Keep-alive (F5) | part | The view is disposed, but the note is in `CounterViewModel.Note`. The note survives because the screen keeps one view model for each tab and all navigations use these instances. A new view model on each navigation drops the note |
| Typed back result | no | `NavigateBack` returns the new current view model, not a result from the old page |
| Shared state (F4) | yes | `AppScreen.Count`, a reactive property on the screen (one screen for each circuit) |

## Decisions

- Mapping. `tab-home` and `tab-counter` call `NavigateAndReset` (stack becomes 1). `go-counter` calls `Navigate` (stack grows). `back` calls `NavigateBack`.
- URL. The URL does not follow. It stays `/`. Blazor has one route (`Shell.razor`), and ReactiveUI routes by view model only. A browser back leaves the app.
- Tab view models. `AppScreen` holds one `HomeViewModel` and one `CounterViewModel`. This is the simplest way to keep page state in ReactiveUI.
- Back guard. After a tab change the stack is 1, and `NavigateBack` cannot execute. `Counter.Back` then logs `Counter: NavigateBack skipped (stack 1)` and does not call the command.
- Base class. The pages inherit `ReactiveComponentBase<T>`, not `TracedPage`, because both are `ComponentBase` classes. The pages log the same three hooks themselves.

## Flows
| Flow | Result | Trace lines |
|---|---|---|
| F1 | browser check pending | |
| F2 | browser check pending | |
| F3 | browser check pending | |
| F4 | browser check pending | |
| F5 | browser check pending | |

Server prerender of `/` (from the console) gives this order: `Router: CurrentViewModel = , stack 0` (two times), `Router: CurrentViewModel = Home, stack 1`, `Router: NavigateAndReset -> Home`, `Router: Navigate -> Home`, `Home: OnInitialized`, `Home VM: WhenActivated`, `Home: Dispose`, `Home VM: WhenActivated disposed (deactivated)`. `NavigateAndReset` also fires the `Navigate` observable. `CurrentViewModel` changes before the command result.

## Problems
- `NU1008`: the repository uses central package versions. Fix: `VersionOverride="27.0.1"` in the csproj, as in `03-brouter`.
- ReactiveUI 27 does not use System.Reactive. `Subscribe(Action)` was not found. Fix: `using ReactiveUI.Primitives` (the `SubscribeExtensions` class).
- `ViewLocator.Current` does not exist in 27. Fix: `ReactiveUI.Binding.ViewLocator.GetCurrent()`.
- `WithViewsFromAssembly` did not register the views, and `/` returned 500 (`No view for HomeViewModel`). Fix: `ConfigureViewLocator` with `Map<HomeViewModel, Home>()` and `Map<CounterViewModel, Counter>()`.
- With the map, `/` still returned 500. Cause: `ResolveView(vm)` with a value of type `IRoutableViewModel` selects the generic overload, which looks up the map by `IRoutableViewModel`. Fix: `ResolveView((object)vm, null)`, which uses the runtime type.
- The namespace `Spike.ReactiveUI` hides the `ReactiveUI` namespace in Razor files. Fix: `@using global::ReactiveUI` in `_Imports.razor`.
- The view locator makes a view instance only to get its type. The host renders a new component of that type. The extra instance is not rendered and not disposed. This is the same in the sample host.

## Browser check (2026-11-21, built-in browser, pane hidden)

The pane was not on screen, so the page drew 0 frames. The functional flows ran; the motion is not checked.

| Flow | Result |
|---|---|
| F1 | pass: Counter, then Home (the URL stays `/`) |
| F2 | pass: Counter |
| F3 | pass after a push: Home. After a tab change the stack has 1 item, and back does nothing |
| F4 | pass: count 3 |
| F5 | pass: note "abc" kept in the view model |

Finding: the URL never changes, so the browser back button leaves the app.
