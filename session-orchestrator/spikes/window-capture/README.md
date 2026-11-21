# Window capture spike (I16)

A check of Windows.Graphics.Capture (`GraphicsCaptureItem` for one window) as a source for tray recordings. Result: the normal screen recorder (`spikes/tray-display/recorder`) stays. This spike keeps the tools for a later issue.

## Result (2026-11-21)

| Check | Result |
|---|---|
| Capture of the tray window (composition mode, WebView2) | Pass: full content, correct transparency |
| Tray covered by another window | Pass: the capture shows the tray, the screen shows the cover |
| Tray off the screen (x = -3000) | Pass |
| Tray minimized | Fail: 0 frames in 5 s |
| Yellow border | Can be turned off (`IsBorderRequired = false`); borderless access was allowed with no prompt |
| Stream of the main screen | 147 frames in 4 s, MP4 made |
| Stream of a layered window off the screen | 114 frames in 4 s, no frame lost |

Not tested: tray motion while the tray is covered or off the screen. WebView2 can stop drawing a window that is not visible.

## Folders

| Folder | Content |
|---|---|
| `capture/` | One frame of one window as a PNG, with alpha counts and a screen copy of the same place (border check) |
| `stream/` | A stream of the main screen or of an off-screen test window, then an MP4 with the real frame timing |
| `tools/` | `run-all.sh` (the 5 tray checks), `win.ps1` (list, move, minimize, topmost with window calls), `cover.ps1` (a red window that covers a window) |

## Use

1. Build: `dotnet build -c Release -o bin` in `capture/` and in `stream/`.
2. Make sure that no other test tray or recorder runs.
3. Tray checks: `bash tools/run-all.sh <Orchestrator.Tray.exe> <out folder>`.
4. Stream: `stream/bin/stream.exe offscreen 4 <folder>`, then `stream/bin/stream.exe mp4 <folder> <out.mp4>`.

## Rules learned

- A capture session sends a frame only when the content changes. A static window gives one frame.
- A window that draws with paint messages gets no paint message when it is fully off the screen. A layered or composition window still sends frames.
- In `win.ps1`, PowerShell reads an argument such as `-3000` as a parameter name in a `param()` block. The script reads `$args` for this reason.
