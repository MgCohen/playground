# Handover: a VSA (Umpire) pipeline for the C# to JS calls of the orchestrator UI

To: the VSA session. From: the motion pipeline (R1) planning session of session-orchestrator.
Ask: give us a guide. Tell us what to change, what works, the expected issues and the warnings, and the order of the steps.
We do not have to fix everything at once. We want to know what will happen.

## Where we are

| Item | Value |
|---|---|
| Repository | `MgCohen/playground`, project folder `session-orchestrator/` |
| Branch | `orch/decision-toolu_013ivNzwGeccMPnQeqxKdFLH` (on top of `orch/session-4a6e31f6`, which is on top of `orch/visual-review`) |
| Draft PR | https://github.com/MgCohen/playground/pull/58 |
| Plan | `session-orchestrator/docs/motion-pipeline-plan.html` (https://claude.ai/artifact/G89uZPfkYsVe5kcHn4daaP) |
| State | Plan only. No code change for R1 yet |
| Stack | .NET 10, Blazor (Razor components), NUnit, bUnit. JS only in small `wwwroot` scripts |

## The current shape

Two hosts run the same Razor components:

| Host | Project | Blazor mode | JS transport |
|---|---|---|---|
| Web server | `src/Orchestrator.Web` | Blazor Server | SignalR (network) |
| Tray | `src/Orchestrator.Tray` | Our own `WebViewManager` in WinForms (`ClearHost.cs`) | WebView2 messages, in process |

The tray has two windows (panel, badge). Each window has its own WebView2 and its own Blazor renderer, so each window has its own `IJSRuntime`.

C# calls JS with `IJSRuntime.InvokeVoidAsync("name", args)`. Each call site has its own copy of the same pattern:

```csharp
// TrayStage.razor: a selector, a property name and a time limit in C#
await JS.InvokeVoidAsync("orchTray.end", ".tray-panel", "clip-path", 900);

// Presence.razor and MainLayout.razor: another helper, and the same error filter in each place
try { await JS.InvokeVoidAsync("orchMotion.settle", root); }
catch (Exception e) when (e is JSException or JSDisconnectedException
                          or TaskCanceledException or InvalidOperationException) { }

// MainLayout.razor: its own "newer call wins" guard (if (mine != run) return;)
// TrayFlow.cs: its own "one run at a time, only the last request runs" queue (busy, pending)
```

| JS function | Call sites | Purpose |
|---|---|---|
| `orchTray.end` | `TrayStage` (2), `BadgeRoot` (3) | Wait for the end of one CSS transition or animation |
| `orchMotion.settle` | `Presence`, `MainLayout` | Wait for all animations of a block |
| `orchTray.frames` | `TrayStage`, `BadgeRoot` | Wait n display frames |
| `orchTray.home` | `TrayStage` | Measure an element, write CSS variables |
| `orchMotion.begin` | `MotionHost` | Start a view transition (browser API) |
| `orchMotion.dot` | `AppBar` | Move the nav dot |

Problems: two wait rules, time limits in C#, errors swallowed with no trace, and one guard or queue written by hand in each place.

## What we want to do

The motion pipeline (R1) makes all motion of the tray one pipeline: times as CSS tokens, one JS wait function, and a C# registry of motion steps. The user decided:

- All motion times are tokens in `motion.css`. The C# registry has only the token names (decided 2026-11-20).
- One JS wait function with modes: settle, property, time, event. One limit from a token. A trace line when a limit ends a wait.
- All visuals and motion are Razor and CSS. C# controls only the windows and the order of the steps.
- No calls from JS to C# for now.

New in this discussion: each C# to JS call becomes a command, and one pipeline runs all commands.

```csharp
public sealed record MotionWait(object Target, WaitMode Mode, string? Arg = null) : JsCommand<WaitResult>;
// The JS name comes from the type (MotionWait → orchMotion.wait). An optional constructor argument overrides it.

var r = await Commands.SendAsync(new MotionWait(stage, WaitMode.Property, "clip-path"));
```

## Requirements for the pipeline

1. One command record for each JS function. There is no wrapper class for each call.
2. The JS function name comes from the command type. An optional override is available.
3. One generic handler calls `IJSRuntime.InvokeAsync<T>` and returns `Result<T>`. A JS error becomes an `Error` with a trace line. Nothing is swallowed.
4. Hooks for the rules of one kind of command. Example: an after hook of `MotionWait` writes a trace line when the wait ends by its limit.
5. A step for each command: the trace of the step name and the time in ms. The self-test reads these lines.
6. Runners:
   - "Newer call wins": a newer envelope with the same key cancels the older one (`MainLayout`).
   - "One at a time, only the last request runs" (`TrayFlow`).
   - All JS calls run on the dispatcher of their renderer.
7. Scope: one pipeline instance for each `IJSRuntime`. That means one for each Blazor Server circuit and one for each tray window.
8. Both hosts: Blazor Server and the tray `WebViewManager`.
9. A unit test can run a command with a fake `IJSRuntime`. The bUnit tests of the components must still pass.

## Why VSA

The VSA handler pipeline (`docs/handler-pipeline.md` of `vsa-template`) already has the parts that we need:

| Our need | VSA part |
|---|---|
| Typed command and result | Command record, `Result<T>`, `Envelope` |
| One handler for all JS commands | `CommandHandler<,>` (a generic `JsCommandHandler<TCommand, TResult>`) |
| A rule for one kind of command | `BeforeCommandHook<>`, `AfterCommandHook<,>` |
| A trace for each command | `AfterStep` |
| "Newer call wins", "one at a time" | A custom `IRunner` |
| A call chain in the trace | `Envelope.Id`, `Parent`, `Depth` |

The other option is our own small pipeline. That copies these parts and adds code that we must keep.

## Questions for the VSA session

1. Does VSA resolve one open generic handler (`JsCommandHandler<TCommand, TResult>`) for each closed `JsCommand<T>` type? If not, what is the correct way?
2. How do we get one pipeline instance for each scope (circuit, window)? `AddPipeline` makes the app instance. Is `.Build(services)` for each scope correct, and how does `ICommandSender` find it?
3. Can a runner switch to the Blazor renderer dispatcher (`ComponentBase.InvokeAsync` or the `Dispatcher` of the renderer)? Is the runner the correct place for that?
4. How do we import Umpire into session-orchestrator for a first version? The user tests with local projects now, so a local package source is possible. The root rule of `playground` says: do not share code between projects, copy it.
5. Which analyzers and architecture rules (`UMP...`) fire on code that does not follow the VSA layout? session-orchestrator does not use `Features/<Feature>/Slices/<Slice>/`. Which warnings can we turn off for this project, and which ones should we fix first?
6. Is there a minimal host part to register modules in the Blazor `IServiceCollection` with no `AppHost`?
7. What are the expected issues with Blazor Server: scoped services, circuit disconnect (`JSDisconnectedException`), prerender (no JS)?

## Expected answer

A short guide, as steps:

1. Import: package or copy, which projects, which version.
2. The first command (`MotionWait`) from the call to the result, in both hosts.
3. The runners for "newer call wins" and "one at a time".
4. The warnings that we will see, and which ones to fix now or later.
5. The risks and the checks for each step.

## Files to read

| File | Content |
|---|---|
| `session-orchestrator/AGENTS.md` | Project rules, stack, commands |
| `session-orchestrator/docs/motion-pipeline-plan.html` | The R1 plan (steps S0 to S9) |
| `session-orchestrator/docs/ui-architecture.html` | View architecture; tab "Prior art" |
| `src/Orchestrator.Web/Components/Tray/TrayFlow.cs` | The order of the windows and the request queue |
| `src/Orchestrator.Web/Components/Tray/TrayStage.razor`, `BadgeRoot.razor` | The JS waits of the tray |
| `src/Orchestrator.Web/Components/Motion/Presence.razor`, `Layout/MainLayout.razor` | The settle waits |
| `src/Orchestrator.Web/wwwroot/motion.js`, `src/Orchestrator.Tray/wwwroot/tray.js` | The JS functions |
| `src/Orchestrator.Tray/ClearHost.cs` | The tray Blazor host (`WebViewManager`) |
