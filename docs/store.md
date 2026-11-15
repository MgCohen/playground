# Store contract (P1)

The mod `mod/orch` and the supervisor share one folder for each session: `store/sessions/<session id>/`. The environment variable `ORCH_STORE` changes the store root.

## Files

| Path | Writer | Content |
|---|---|---|
| `enabled` | Supervisor, or the `/orch-enable` command | Empty file. The mod does nothing in a session without it |
| `lease.json` | Mod | `{ holder, since, reason }`. `holder` is `live`, `supervisor` (from `ORCH_HOLDER`) or `none` |
| `heartbeat.txt` | Mod | ISO time, at activation and every 3 s while the process is alive |
| `status.json` | Mod | `{ state: running \| idle, turnId, at }` for the main loop |
| `inbox/<name>.txt` | Supervisor, UI | One user turn. The mod submits names in numeric order (`1.txt`, `2.txt`, or `<ms>-<random>.txt`) |
| `inbox/<name>.json` | Supervisor, UI | `{ "text": "..." }`, the same as a `.txt` file |
| `cursor.txt` | Mod | The name of the last inbox file that the mod submitted |
| `mirror/<ms>-<uuid>.json` | Mod | `{ door, origin, uuid, message }` for the doors `prompt`, `response` and `tool-result`. `message` is JSON, cut to 4000 characters |
| `decisions/<id>.json` | Mod | One decision (below) |
| `answers/<id>.json` | Supervisor, UI | The answer (below) |
| `delivered/<id>.json` | Mod | `{ by, at }`. `by` is `tool-result`, `inbox` or `app-card` |
| `report.json`, `reports/<ms>.json` | Mod | `{ state: working \| blocked \| done, summary, details, at }` from the `report` tool |
| `log-<holder>.txt` | Mod | Log lines of one holder (`live` or `supervisor`), cut to the last 100 000 characters |
| `meta.json` | Supervisor, `spawn_child` | `{ id, parent, title, cwd, createdBy, createdAt, started }`. A session with `createdBy` and no `started` gets a new SDK session with this id |
| `blocked.json` | Mod, supervisor | `{ reason, since }`. `reason` is `null` when the session is not blocked |
| `forwarded.json` | Supervisor | `{ at }` of the last child report that the supervisor sent to the parent |

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

## Supervisor files

| Path | Content |
|---|---|
| `store/supervisor/log.txt` | One line for each turn, lease release and report forward |

## HTTP API (supervisor, `http://127.0.0.1:7777`)

| Call | Body | Result |
|---|---|---|
| `GET /api/sessions` | | Each enabled session: title, parent, holder, live, running, status, report, blocked, open decisions |
| `GET /api/decisions` | | All open decisions |
| `POST /api/sessions` | `{ title, cwd, brief, parent? }` | `{ id }`. The supervisor starts the session with the brief |
| `POST /api/sessions/<id>/send` | `{ text }` | `{ inbox }`. A new inbox file |
| `POST /api/decisions/<session>/<id>` | `{ answer }` or `{ answers }` | `{ answered }`. The answer file |

## Rules

1. Write each file in one write. Do not change a file in `inbox/` or `answers/` after you write it.
2. Give each inbox file a new name that sorts after all earlier names.
3. Only one writer has the lease. The mod delivers inbox files and answers only with holder `live`. With any other holder, the supervisor delivers them with the Agent SDK.
4. A lease `live` counts as live while the heartbeat or the lease is less than 15 s old. After that, the supervisor releases it (`none`). The mod takes a released lease back at its next beat.
5. When a child reports `done` or `blocked`, the supervisor writes the inbox file `Report from child "<title>" (<id>): <state>. <summary>` to the parent.
