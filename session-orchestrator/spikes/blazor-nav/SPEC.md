# Blazor navigation spikes: the spec

Each spike builds the same small app with one library. All spikes use the same pages, the same test IDs and the same result template. Then we can compare the libraries with the same flows.

## The app

Two pages, one app bar and one trace panel. Blazor Web App, .NET 10, interactive server render mode, no backend.

```
 Home ("/")                          Counter ("/counter")
┌────────────────────────────┐      ┌────────────────────────────┐
│ Home                       │      │ Counter             [Back] │  back
│                            │      │                            │
│ [ Add one ]          add   │      │ Count: 3            count  │
│ [ Open counter ] go-counter│      │ Note: [__________]  note   │
│                            │      │                            │
├────────────────────────────┤      ├────────────────────────────┤
│ Trace (last lines)   trace │      │ Trace                      │
├────────────────────────────┤      ├────────────────────────────┤
│ (Home)        Counter      │      │  Home        (Counter)     │
│ tab-home      tab-counter  │      │                            │
└────────────────────────────┘      └────────────────────────────┘
```

The words on the right side of each box are the `data-test` values. Use them exactly.

| `data-test` | Element | Page |
|---|---|---|
| `page-home`, `page-counter` | The root element of the page | Home, Counter |
| `add` | Button "Add one": adds 1 to the shared count | Home |
| `go-counter` | Button "Open counter": navigates in C# code to Counter | Home |
| `count` | The shared count | Counter |
| `note` | A text input with local page state only (not in a store) | Counter |
| `back` | Button "Back": goes back in C# code | Counter |
| `tab-home`, `tab-counter` | App bar items | All |
| `trace` | The trace panel (`TraceView` from the shared project) | All |

## The flows

Do each flow in this order. Write the result of each flow in `RESULT.md`.

| Flow | Steps | What it shows |
|---|---|---|
| F1 Tab change | Click `tab-counter`, then `tab-home` | The app bar navigation and its motion |
| F2 Push | On Home, click `go-counter` | Navigation from C# code and its motion |
| F3 Back | On Counter, click `back` | Back from C# code and its motion (reverse direction if the library knows it) |
| F4 Shared state | On Home, click `add` 3 times, then `go-counter` | `count` shows 3 |
| F5 Page state | On Counter, type "abc" in `note`, go to Home, come back | Is "abc" kept? (keep-alive) |
| F6 Typed result | On Home, click `pick`; on the Picker page, click `pick-red` | The Picker closes, Home shows `picked` = "Red". The result comes back typed from C# code, not from a URL or a store |
| F7 Leave guard | On Counter, type "abc" in `note`, click `tab-home` | The page can wait or cancel the leave. Show an in-page bar `guard` with `guard-stay` and `guard-leave` (no JS `confirm`) |

F6 and F7 are for spikes 07 and later. The Picker page has the root `page-picker` and the buttons `pick-red` and `pick-blue`. It has no tab and no URL of its own if the shape allows that.

## Rules

1. **Shared base.** Reference `../Shared/NavSpike.Shared.csproj`. Use its `spike.css`, `Trace`, `TraceView`, `CounterState` and `TracedPage`. Do not copy them.
2. **Same look.** Use the markup of `01-native` for the pages and the app bar. Change only what the library needs.
3. **Hooks.** Log each lifecycle hook of the library to `Trace` with its exact name, for example `Trace.Add("Counter: OnActivated")`. `TracedPage` already logs `OnInitialized`, the first `OnAfterRender` and `Dispose`.
4. **Motion.** Use the transition support of the library if it has one: push slides in from the right, back slides in from the left, tab change fades. Use 300 ms. If the library has no motion support, do a plain swap. Do not write our own animation code.
5. **State.** Use the state feature of the library if it has one (store, keep-alive, view model). If not, use `CounterState` (a scoped service), as `01-native` does.
6. **Minimal.** No backend, no extra pages, no styling work. Use the smallest code that shows the feature.
7. **Port.** Each spike has its own port: `dotnet run --urls http://localhost:<port>`.
8. **No commit.** Do not commit and do not push.

## Spikes

| Folder | Library | Port | Why it is in |
|---|---|---|---|
| `01-native` | Blazor `Router`, `NavigationManager` | 5101 | The baseline (official) |
| `02-viewtransition` | Toolbelt.Blazor.ViewTransition | 5102 | Author is a known Blazor MVP; Microsoft points to it in dotnet/aspnetcore#10389 |
| `03-brouter` | Bit.Brouter (bitplatform) | 5103 | 1.4K stars; hooks, keep-alive and transitions |
| `04-reactiveui` | ReactiveUI `RoutingState` with ReactiveUI.Blazor | 5104 | 8.6K stars; a view-model stack |
| `05-masa` | MASA Blazor `PPageStack` | 5105 | 1.3K stars, 1.3M downloads; a mobile page stack |
| `06-antdesign` | Ant Design Blazor `ReuseTabs` | 5106 | 6.2K stars; keep-alive pages |
| `07-pagehost` | Our shape: default `Router`, our `PageHost` in `<Found>`, our `Navigator` push stack | 5107 | No library. Keeps all default parts |
| `08-siterouter` | Our shape: no `Router`, one `NavState` object from the URL (the Oqtane `SiteRouter` model), our `Navigator` | 5108 | No library. The URL only at the edge |

Spikes 07 and 08 have no library, so rule 4 changes for them: the host plays the motion with CSS classes and waits in C#. Use no new JavaScript file.

## Result template (`RESULT.md` in each folder)

```markdown
# <Library> <version>

| Item | Value |
|---|---|
| Package | <NuGet id> <version> |
| Setup | <files changed, in one line> |
| Lines of spike code | <number, without the shared base> |

| Capability | Result | How |
|---|---|---|
| Swap pages | yes/part/no | |
| Transition | yes/part/no | |
| Direction (push/back) | yes/part/no | |
| Lifecycle hooks | yes/part/no | <exact hook names> |
| Cancel a leave | yes/part/no | |
| Keep-alive (F5) | yes/part/no | |
| Typed back result | yes/part/no | |
| Shared state (F4) | yes/part/no | |

## Flows
| Flow | Result | Trace lines |
|---|---|---|
| F1 | | |
| F2 | | |
| F3 | | |
| F4 | | |
| F5 | | |

## Problems
- <problem, and what you did>
```

## Known traps (from `01-native`)

- Copy `01-native/Program.cs`. It has `builder.WebHost.UseStaticWebAssets()`: the spike runs as Production, and without this line the shared `spike.css` and `blazor.web.js` come back empty.
- `Directory.Build.props` sets `RunWorkingDirectory`, so `dotnet run --project <folder>` works from the repository root.
- An exception in a navigation hook ends the Blazor circuit, and then no button works. In a location changing handler, make `context.TargetLocation` absolute before `ToBaseRelativePath`.
- Count lines of spike code without empty lines and without the shared base.
