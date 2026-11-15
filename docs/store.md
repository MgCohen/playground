# Store contract (P1)

The mod `mod/orch` and the supervisor share one folder for each session: `store/sessions/<session id>/`. The environment variable `ORCH_STORE` changes the store root.

## Files

| Path | Writer | Content |
|---|---|---|
| `enabled` | Supervisor, or the `/orch-enable` command | Empty file. The mod does nothing in a session without it |
| `lease.json` | Mod | `{ holder, since, reason }`. `holder` is `live`, `supervisor` (from `ORCH_HOLDER`) or `none` |
| `heartbeat.txt` | Mod | ISO time, every 3 s while the process is alive |
| `status.json` | Mod | `{ state: running \| idle, turnId, at }` for the main loop |
| `inbox/<name>.txt` | Supervisor, UI | One user turn. The mod submits names in numeric order (`1.txt`, `2.txt`, or `<ms>-<random>.txt`) |
| `inbox/<name>.json` | Supervisor, UI | `{ "text": "..." }`, the same as a `.txt` file |
| `cursor.txt` | Mod | The name of the last inbox file that the mod submitted |
| `mirror/<ms>-<uuid>.json` | Mod | `{ door, origin, uuid, message }` for the doors `prompt`, `response` and `tool-result`. `message` is JSON, cut to 4000 characters |
| `decisions/<id>.json` | Mod | One decision (below) |
| `answers/<id>.json` | Supervisor, UI | The answer (below) |
| `delivered/<id>.json` | Mod | `{ by, at }`. `by` is `tool-result`, `inbox` or `app-card` |
| `report.json`, `reports/<ms>.json` | Mod | `{ state: working \| blocked \| done, summary, details, at }` from the `report` tool |
| `log.txt` | Mod | Log lines, cut to the last 100 000 characters |

## Decision

```json
{
  "id": "toolu_...",
  "session": "<session id>",
  "kind": "ask_user | AskUserQuestion",
  "question": "Pick a fruit",
  "options": ["PLUM", "LIME"],
  "questions": [ "AskUserQuestion only: the questions as the tool got them" ],
  "risk": "low | medium | high",
  "defaultOption": "PLUM",
  "mode": "wait | pending",
  "waitSeconds": 0,
  "asked": "2026-11-15T18:40:00.000Z"
}
```

A decision is open while `delivered/<id>.json` does not exist.

## Answer

| Kind | `answers/<id>.json` |
|---|---|
| `ask_user` | `{ "answer": "LIME" }` |
| `AskUserQuestion` | `{ "answers": { "<question text>": "<label>" } }` |

## Delivery

| Mode | What the mod does with the answer |
|---|---|
| `wait` | The tool returns `ANSWER: <text>`. If no answer comes in `waitSeconds` (580 maximum), the mod changes the mode to `pending` |
| `pending` | The mod submits the turn `Answer to decision <id> (<question>): <text>` when the answer file appears |
| `AskUserQuestion` | The tool waits 300 s, then returns the answers. With no answer, the app card shows and `delivered` is `app-card` |

## Rules

1. Write each file in one write. Do not change a file in `inbox/` or `answers/` after you write it.
2. Give each inbox file a new name that sorts after all earlier names.
3. Only one writer has the lease. A session with holder `supervisor` gets turns from the Agent SDK, not from the inbox.
