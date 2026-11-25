# Spike: ping pong

One engine process and five heads with the same Razor components. Results: `docs/plan.md`, "V2 spike: ping pong".

| Project | Job |
|---|---|
| `PingPong.Core` | The game library and the HTTP and SSE client |
| `PingPong.Engine` | The process that runs the game (port 7931) |
| `PingPong.Ui` | The shared Razor components and CSS |
| `PingPong.Web` | The web head (port 7932) |
| `PingPong.Win` | The Windows code of the three Windows heads |
| `PingPong.Widget` | The tray app: a transparent ball that grows into a panel |
| `PingPong.Notify` | A card for each hit |
| `PingPong.App` | The Windows main window (WinForms `BlazorWebView`) |
| `PingPong.Android` | The MAUI Android app |

## Run

1. Build each project: `dotnet build PingPong.<name> -c Release`.
2. Start the Windows parts: `./run.ps1`.
3. Send a test command to a head: `python probe.py widget state`.
4. Build the Android app with the `maui-android` workload. It connects to `10.0.2.2:7931`.

Stop each head with `python probe.py <head> exit`, or end the processes `PingPong.*`.
