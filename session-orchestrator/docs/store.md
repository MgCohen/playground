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
| `delivered/<id>.json` | Mod | `{ by, at }`. `by` is `tool-result`, `inbox` or `supervisor` |
| `report.json`, `reports/<ms>.json` | Mod | `{ state: working \| blocked \| done, summary, details, at }` from the `report` tool |
| `log-<holder>.txt` | Mod | Log lines of one holder (`live` or `supervisor`), cut to the last 100 000 characters |
| `meta.json` | Supervisor, `spawn_child` | `{ id, parent, title, cwd, createdBy, createdAt, started }`. A session with `createdBy` and no `started` gets a new SDK session with this id |
| `blocked.json` | Mod, supervisor | `{ reason, since }`. `reason` is `null` when the session is not blocked |
| `docs-checks/<id>.json` | Supervisor | `{ answered, answer, source, at }`: the result of the docs check of one decision |
| `reviews/<id>.json` | Supervisor | `{ verdict: approve \| change \| refuse, text, at }` |
| `forwarded.json` | Supervisor | `{ at }` of the last child report that the supervisor sent to the parent |

## Decision

```json
{
  "id": "toolu_...",
  "session": "<session id>",
  "kind": "ask_user",
  "question": "Pick a fruit",
  "options": ["PLUM", "LIME"],
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
| `ask_user` | `{ "answer": "LIME", "by": "user \| docs \| policy", "source": "...", "note": "..." }`. Only `answer` is necessary. The mod and the supervisor add `note` to the answer text that the model gets |

## Delivery

| Mode | What the mod does with the answer |
|---|---|
| `wait` | The tool returns `ANSWER: <text>`. If no answer comes in `waitSeconds` (580 maximum), the mod changes the mode to `pending` |
| `pending` | The mod submits the turn `Answer to decision <id> (<question>): <text>` when the answer file appears |

## Supervisor files

| Path | Content |
|---|---|
| `store/supervisor/log.txt` | One line for each turn, lease release, report forward, docs answer and default |
| `store/policy.json` | The policy in use. The supervisor writes it at start from `config/policy.default.json` if it does not exist. The mod reads the levels from it |
| `store/ledger.jsonl` | One line for each answer from the docs, each default and each review |
| `store/events/<day>/<id>.json` | One file for each event: `{ id, at, session, kind, from, to, data, refs }`. Written by the supervisor and the mod (spike version). `GET /api/events` returns all events in order |

## HTTP API (supervisor, `http://127.0.0.1:7777`)

| Call | Body | Result |
|---|---|---|
| `GET /api/sessions` | | Each enabled session: title, parent, holder, live, running, status, report, blocked, open decisions |
| `GET /api/decisions` | | All open decisions |
| `POST /api/sessions` | `{ title, cwd, brief, parent? }` | `{ id }`. The supervisor starts the session with the brief |
| `POST /api/sessions/<id>/send` | `{ text }` | `{ inbox }`. A new inbox file |
| `POST /api/decisions/<session>/<id>` | `{ answer }` | `{ answered }`. The answer file, with `by: user` |
| `POST /api/decisions/<session>/<id>/review` | `{ verdict: approve \| change \| refuse, text }` | `{ inbox }`. The review instruction goes to the session |
| `GET /api/ledger` | | All ledger lines |

## Questions to the user

The user answers in free text, never with a click. The `options` of a decision are suggestions only.

| Layer | What it does |
|---|---|
| `~/.claude/settings.json` | `permissions.deny: ["AskUserQuestion"]` removes the built-in question tool from all sessions |
| Mod, fallback | `tool.describe` moves `AskUserQuestion` behind ToolSearch with "Do not use this tool". `tool.call` refuses it: in a normal session the model asks in its reply text; in an orchestrator session it calls `ask_user` |

## Decision policy

`store/policy.json`:

```json
{
  "levels": [
    { "name": "low", "description": "...", "timeoutSeconds": 1800, "checkDocs": true },
    { "name": "medium", "description": "...", "timeoutSeconds": 14400, "checkDocs": true },
    { "name": "high", "description": "...", "timeoutSeconds": null, "checkDocs": false }
  ],
  "docs": ["AGENTS.md", "CLAUDE.md", "README.md", "docs/"],
  "onDefault": "stacked-pr"
}
```

| Field | Meaning |
|---|---|
| `levels` | Any number of levels, from the least strict to the most strict. The model picks one for each decision from the `description`. An unknown level counts as the last level |
| `timeoutSeconds` | After this time with no answer, the supervisor applies `defaultOption`. `null`: never. A decision with no `defaultOption` always waits |
| `checkDocs` | The supervisor first asks a read-only check if the `docs` paths in the session folder answer the question. If yes, that answer applies at once, with `by: docs` and the source |
| `onDefault` | What a default answer tells the session to do: `stacked-pr` (a branch `orch/decision-<id>` on top of the current branch, a commit, and a draft PR if there is a remote), `commit` (a separate commit) or `none` |

A review of a default decision:

| Verdict | Instruction to the session |
|---|---|
| `approve` | Mark the PR ready for review. Do not merge |
| `change` | Change the decision branch, commit, rebase each branch above it |
| `refuse` | Close the PR and each PR above it, keep the branches, go back to the base branch and continue in the direction of the user |

## Rules

1. Write each file in one write. Do not change a file in `inbox/` or `answers/` after you write it.
2. Give each inbox file a new name that sorts after all earlier names.
3. Only one writer has the lease. The mod delivers inbox files and answers only with holder `live`. With any other holder, the supervisor delivers them with the Agent SDK.
4. A lease `live` counts as live while the heartbeat or the lease is less than 15 s old. After that, the supervisor releases it (`none`). The mod takes a released lease back at its next beat.
5. When a child reports `done` or `blocked`, the supervisor writes the inbox file `Report from child "<title>" (<id>): <state>. <summary>` to the parent.
