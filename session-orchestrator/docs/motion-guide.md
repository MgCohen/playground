# Motion guide

How to add, change, use and check a motion of the UI (the web pages and the tray). Read this guide before you change a motion. The design of the pipeline is in `motion-pipeline-plan.html`; the look of each motion is in `motion-design.html`.

## The rule

CSS owns each motion: its time, its curve, its target and what ends it. C# only sets a state class, waits for the end of the step, and orders the steps. C# calls JS only through a command record.

```text
C# (component)                         CSS (motion.css, tray.css, app.css)           JS (motion.js)
──────────────                         ───────────────────────────────────           ──────────────
1. set the state class   ───────────▶  rule of the class: transition/animation
   (is-open, out-r, ...)               with a token (var(--t-grow))
2. SendAsync(MotionWait(step))  ─────────────────────────────────────────────▶  orchMotion.wait:
                                       [data-motion~="grow"]                     finds the target,
                                       { --motion-wait: clip-path; }  ◀───────── reads --motion-wait,
                                                                                 waits for those motions
3. result { by, ms }  ◀──────────────────────────────────────────────────────── by "end" | "cancel" |
4. next step                                                                     "limit" | "hidden" | "missing"
```

## Where each part is

| Part | File | Owner |
|---|---|---|
| Time and curve tokens (`--t-*`, `--ease-*`) | `src/Orchestrator.Web/wwwroot/motion.css`, block `:root` | CSS |
| Reduced motion (each duration token 1 ms) | `motion.css`, the last block | CSS |
| Motion rules of the views | `motion.css`, `app.css`; the tray: `src/Orchestrator.Tray/wwwroot/tray.css` | CSS |
| Step rules (`[data-motion~="name"]`, `--motion-wait`, spec ID) | `motion.css` (pages, deck), `tray.css` (tray) | CSS |
| Step target | the element with `data-motion="name"` in a `.razor` file | Razor |
| Registry of the page steps (name, state class) | `src/Orchestrator.Web/Features/Motion/Domain/MotionSteps.cs` | C# |
| Registry of the window steps (tray) | `Features/Motion/Domain/WindowSteps.cs` | C# |
| The wait command | `Features/Motion/Slices/Wait/MotionWait.cs`, `WaitMode`, `WaitResult` | C# |
| The other JS commands | `Features/Motion/Slices/*`, `Features/Speech/Slices/*` | C# |
| The JS command pipeline (handler, trace, "newer call wins") | `src/Orchestrator.Web/Plugins/Js/` | C# |
| The wait function and `orchMotion.ms` | `src/Orchestrator.Web/wwwroot/motion.js` | JS |
| The order of the tray windows | `Components/Tray/TrayFlow.cs` | C# |

## Recipes

### 1. Change a time or a curve

1. Change the token in the `:root` block of `motion.css`. Do not write a time in a rule, a script or C#.
2. If the motion has no token yet, add one with a name `--t-<thing>`, then use `var(--t-<thing>)` in the rule.
3. If the token is a duration, add it with `1ms` to the reduced motion block. The test `Reduced_motion_sets_each_duration_token_to_1ms` fails if you forget it.
4. If the token is a dwell time (how long a thing shows) or a limit, do not add it to the reduced block. Add its name to `Kept` in `MotionArchitectureTests.cs`.

### 2. Add a motion that nothing waits for

Use this for a hover, a press, a fade-in on the first show, a ring draw.

1. Write the rule in `motion.css` (or `tray.css`) with tokens. Add a comment with the spec ID of `motion-design.html`.
2. Set the state class in the `.razor` file, as for any other class.
3. No C# wait, no registry row.

### 3. Add a motion step that C# waits for

Use this when the next action must come after the motion: a window hides, an act runs, a block goes away.

1. Add a row to `MotionSteps.cs`:
   ```csharp
   public static readonly MotionStep SheetOut = new("sheet-out", "is-leaving");
   ```
   Add it to `All`.
2. Mark the target in the `.razor` file. An element can have more than one step name:
   ```html
   <div class="sheet" data-motion="sheet-out">
   ```
3. Write the step rule in the CSS, with the spec ID as a comment on the same line:
   ```css
   [data-motion~="sheet-out"] { --motion-wait: transform opacity; } /* M5 */
   ```
   - `--motion-wait` names the transition properties or the animation names that end the step, one or a list.
   - With no `--motion-wait`, the step waits for each finite motion of the target and its children (settle).
4. In C#, set the class, render, then wait:
   ```csharp
   phase = "is-leaving";
   StateHasChanged();
   await Commands.SendAsync(new MotionWait(MotionSteps.SheetOut.Target, WaitMode.Step), default);
   ```
   Inject `ICommandSender Commands`. If you have an `ElementReference`, give it as the target: `new MotionWait(root, WaitMode.Step)`.
5. Run the unit tests. `Registry_matches_motion_css` fails if the row, the `data-motion` mark and the CSS rule do not agree, or if the spec ID is not in `motion-design.html`.

### 4. Change a step

| Change | Where |
|---|---|
| What ends the step | `--motion-wait` in the step rule |
| The element that moves | move `data-motion` in the `.razor` file |
| The look or the time | the motion rule and the tokens (recipe 1) |
| The state class | the row in `MotionSteps.cs` and the class in the component |
| The order of the steps | the component (or `TrayFlow` for the tray windows) |

### 5. Wait in other ways

`MotionWait(target, mode, arg)`:

| Mode | `arg` | Ends when |
|---|---|---|
| `Step` | none | the motions named by `--motion-wait` of the target end (settle if empty). Use this for each registry step |
| `Settle` | none | each finite motion of the target and its children ends |
| `Property` | `"clip-path"` or `"clip-path scale"` | those transitions or animations of the target and its children end |
| `Time` | `"--t-peek-show"` | the time of the token |
| `Event` | `"frames:2"` or a DOM event name | n display frames, or the event |

Each wait ends at the latest after `--t-limit` (`--t-frame-limit` for frames). With no motion found, the wait looks again for 2 frames, because the class can come in the render after the call.

The result:

| `By` | Meaning | What to do |
|---|---|---|
| `end` | the motion ended | go on |
| `cancel` | the browser stopped the motion (the state changed) | go on; a newer step owns the element |
| `limit` | the limit ended the wait | a defect: the trace has a "wait at limit" line. Examine `--motion-wait` |
| `hidden` | the page is hidden, nothing moves | go on |
| `missing` | no target | a defect: examine `data-motion` and the selector |

A JS error comes back as `waited.Error` (code `js.failed` or `js.canceled`), not as an exception. The trace has it.

"Newer call wins": give the command a `Key`. A newer call with the same key cancels the older one, which ends with `js.canceled`:

```csharp
var waited = await Commands.SendAsync(new MotionWait(page, WaitMode.Step) { Key = MotionSteps.TabIn.Name }, default);
if (waited.Error?.Code == JsErrors.Canceled) return;
```

### 6. Read a token in C#

```csharp
var undo = await MotionTimes.Of(Commands, "--t-undo");   // TimeSpan?, null if the page did not answer
```

C# reads the value from the page. Do not copy it into C#.

### 7. Add a C# to JS call (any call, not only motion)

1. Write a record. The JS name comes from the type: the first word is the object, the rest is the function (`MotionWait` → `orchMotion.wait`, `SpeechFocus` → `orchSpeech.focus`). The constructor parameters are the arguments, in order:
   ```csharp
   public sealed record SpeechFocus(string Id, bool Voice) : JsCommand<Unit>;      // no result: Unit
   public sealed record MotionMs(string Token) : JsCommand<double>;               // a result
   public sealed record OldName(string X) : JsCommand<Unit>("orchLegacy.run");    // a name that is not from the type
   ```
2. Put it in a slice of its feature: `Features/<Feature>/Slices/<Name>/<Record>.cs`.
3. Write the function in the script: `window.orchSpeech = { focus: function (id, voice) { ... } };`.
4. Send it: `await Commands.SendAsync(new SpeechFocus("sheet-text", true), default);`.
5. Do not write a handler. One handler (`JsCommandHandler<TResult>`) runs all JS commands.
6. The tests `Js_names_exist` and `No_direct_js_calls` check the name and that no file outside `Plugins/Js` uses `IJSRuntime`.

### 8. The tray windows

The tray has page steps (a page plays a motion) and window steps (a window shows or hides). C# owns the window steps, because C# controls the windows.

- A window step is a row of `WindowSteps.cs`. `TrayFlow` runs it through `Window(phase)`.
- A page step runs through `Page(...)`.
- The trace writes each window step with its event, its time and the window gap.
- One request runs at a time; a request during a run waits, and only the last one runs.
- A badge notice comes only for a card that arrives while the tray is closed, one time (`TrayNotices`).

### 9. The deck

The script `swipe.js` only follows the pointer and clicks the act buttons. C# starts the throw and the tuck (the classes `out-r`, `out-l`, `tuck`), waits for the step `deck-throw` or `deck-tuck`, then does the act. Do not add a timer to `swipe.js`; `No_motion_timer_in_scripts` fails.

## Check a change

1. Unit tests: `dotnet test --solution Orchestrator.slnx`. Set `GITHUB_TOKEN` (`gh auth token`) before the first restore.
2. Tray self-test: `dotnet run --project src/Orchestrator.Tray -- --selftest <folder>`. Add `--reduced` for reduced motion. The checks of the pipeline:

   | Check | Fails when |
   |---|---|
   | `no-wait-at-limit` | a wait ends by its limit, or a JS call fails |
   | `steps-traced` | a tray step does not end by the end of its motion |
   | `window-steps` | a window step is not in the trace |
   | `reduced-flow-time` | with `--reduced`, a dock or an open takes 300 ms or more |

3. A recording of a scenario, before and after the change, from the project folder:
   ```bash
   spikes/tray-display/tools/scenario.sh dock-open dock-open-after 12
   ```
   The scenarios are in `src/Orchestrator.Tray/SelfTest.Scenarios.cs`. The log `<out>/<name>/scenario.txt` has each step, each phase change and each trace line. A name that ends with `-reduced` turns on reduced motion. Make sure that no window covers the tray during the recording.
4. The trace: each JS call writes a line, for example:
   ```text
   js orchMotion.wait("[data-motion~="grow"]", Step, null) #5: by end after 342 ms in 343 ms
   window badge-show: shown in 35 ms, gap 0 ms
   ```
   The tray writes it to its trace with `--instance <name>` or `--demo`.

## Do not

- Do not write a time (`.3s`, `300`, `TimeSpan.FromMilliseconds`) in a view file. Use a token. (`Motion_times_are_tokens`)
- Do not call `IJSRuntime` in a component. Send a command. (`No_direct_js_calls`)
- Do not use `setTimeout` to wait for a motion. Use `orchMotion.wait`.
- Do not set `transition: none` or `animation: none` for reduced motion. The duration tokens are 1 ms, so each end event still comes.
- Do not catch and ignore a JS error. The result has the error, and the trace has the line.

## Known limits

- Umpire gives the finding UMP020301 for each JS command record: the VSA rule wants a handler in the slice. This is an Umpire gap; the test `R1_folders_have_no_findings` allows only this finding.
- Pointer motion (a drag) stays in the script: C# cannot follow each frame.
- A spring is a `cubic-bezier`; CSS has no physics.
- The window gap has a reference token (`--t-window-gap`), but no code waits for it (I15).
