# Session orchestrator: implementation plan

## Goal

One local system controls all Claude Code sessions of the user:

- It starts, nests, watches, stops and archives sessions.
- It delivers each message. No message gets lost.
- It keeps one flat queue of decisions for the user, with the path of each decision.
- It applies a default decision after a timeout, by risk level. It records the decision and makes a commit or a tag.
- A phone UI shows the queue (swipe cards) and the state of each session.

## Main rule: one writer for each session (the lease)

The spike `Spike: two writers` showed this fact. If the app process and a CLI process write to one session, the transcript gets two branches, and each writer sees only its own branch.

| Lease holder | Who sends a turn | How |
|---|---|---|
| `live` | The process that has the session open (desktop app, phone through Remote Control, terminal) | The supervisor writes to the inbox. The mod in that process submits the turn |
| `supervisor` | The supervisor | Agent SDK resume of the session id |
| `none` | Nobody | The next writer takes the lease |

A hand-off only moves the lease. The transcript stays one branch.

## Parts

```
orchestrator/
├─ mod/            Claude Code mod; loads in each session; inert unless the session is enabled
│   inbox → $.prompt.submit, mirror rows, heartbeat, status, lease, tools (ask_user, report, spawn_child)
├─ supervisor/     Node process, Agent SDK
│   store, scheduler, heartbeat check, decision policy, SDK runner, HTTP API
├─ ui/             local web app (phone through a tunnel): session tree, decision cards
└─ store/          files (later SQLite): sessions/<id>/{enabled, lease, status, inbox, mirror, decisions, answers}
```

## Phases

| Phase | Work | Done when |
|---|---|---|
| P0 | Spikes S1 to S7 (below) | Each spike has a result in this file |
| P1 | Mod: inbox, mirror, heartbeat, lease, `ask_user`, `report` | A desktop session gets inbox turns and its rows are in the store |
| P2 | Supervisor: store, SDK runner, lease rules, heartbeat check, `spawn_child` | The supervisor starts a session tree and resumes idle sessions |
| P3 | Decision policy: risk levels, timeouts, default, record, commit or tag | An unanswered low-risk decision gets its default and a record |
| P4 | UI: session tree, decision cards, answer to `answers/`; tunnel to the phone | You answer a decision from the phone |
| P5 | Move the vsa-template work to the orchestrator | One stream runs under the orchestrator from start to merge |

## Spikes (P0)

| Id | Question | Method |
|---|---|---|
| S1 | Does a mod from `CLAUDE_CODE_PLUGIN_DIRS` load in a desktop app session, in `claude -p`, and in an Agent SDK session? | Mod writes `lease.json` at `session.start` |
| S2 | Does `$.prompt.submit` from a timer start a turn in an idle desktop session? Does the phone show the turn? | Write `inbox/1.txt`, read the transcript |
| S3 | Do `session.append`, `turn.start` and `turn.complete` give the rows and the state? | Read `mirror/` and `status.json` |
| S4 | Can a mod tool wait minutes for an answer file? | `ask_user` with an answer file after 30 s and after 150 s |
| S5 | Lease hand-off: app writes, app closes, supervisor resumes, app opens again. Is the transcript one branch? | Read the parent chain of the transcript |
| S6 | Does the Agent SDK run with the local login, resume by id, and stream events? | Small Node script |
| S7 | Does `session.end` fire when the app closes or archives a session? | Read `lease.json` after an archive |

## Results (2026-11-15)

| Id | Result | Evidence |
|---|---|---|
| S0 | ❌ Two writers break a session. The app keeps the conversation in memory; a CLI turn makes a second branch | Session `dee17212`: app turn 4 lost CHERRY |
| S1 | ✅ The mod loads in the desktop app, in `claude -p` and in the Agent SDK, through `CLAUDE_CODE_PLUGIN_DIRS` in the `env` block of `~/.claude/settings.json` | `lease.json` written in all three |
| S2 | ✅ An inbox file starts a turn in an idle app session after about 2 s. The transcript stays one branch | Session `6ad6c434`, turn 3 CHERRY |
| S3 | ✅ `session.append` gives each row (with `door` and `origin`); `turn.start` and `turn.complete` give running and idle. `session.receive` also shows peer messages | `mirror/`, `status.json`, `log.txt` |
| S4 | ❌ A mod tool cannot wait. The engine stops a `tool.call` hook after about 10 s | The hook waited 152 s; the model got "no tool.call hook answered" |
| S4b | ✅ Non-blocking `ask_user`: it returns PENDING, the session ends its turn, and the answer arrives as an inbox turn | Turn 6 FIG |
| S5 | ✅ Lease hand-off: app → archive → SDK resume → unarchive → app. The app sees the SDK turn, and the transcript has one leaf | Turns 7 GRAPE and 8 HONEYDEW |
| S6 | ✅ The Agent SDK uses the local login (`apiKeySource: none`), takes a fixed `sessionId`, resumes, streams events and loads the mod | `spikes/sdk/s6.mjs` |
| S7 | ✅ An archive stops the app process and fires `session.end` | `lease.json` holder `none` |

### Design changes from the spikes

1. `ask_user` is non-blocking. The supervisor sends the answer as an inbox turn. No process waits for the user.
2. The supervisor takes a lease from the app with an archive, and gives it back with an unarchive. `session.end` and `session.start` move the lease in the store.
3. A mod reload runs `session.start` again, so the mod state must come from the store, not from memory.
4. The mirror must keep only the `prompt`, `response` and `tool-result` doors. The other doors are system rows.
5. `canUseTool` did not fire, because the user settings use auto mode. Approvals go through `ask_user` and the risk policy, not through permission prompts.

### Second round (2026-11-15)

| Id | Result | Evidence |
|---|---|---|
| S4c | ✅ A tool can block for minutes. The mod waits inside `$.process.run`, and a `$` call in flight does not count against the hook budget. `$.process.run` takes `timeoutMs` up to 600 000 | Session `3df803ac`: `ask_user` waited 120 s, `ANSWER: DATE` |
| S8 | ✅ Redirect of the built-in `AskUserQuestion`: the mod writes the questions to the store, waits, and returns `{ questions, answers }`. With no answer in time, it calls `next(e)` and the app card shows | MANGO from the store, no card |
| S9 | ✅ Six SDK sessions in parallel: 17 s, each with a correct lease, state and mirror | `spikes/sdk/s9.mjs` |
| S10 | ✅ The phone (Remote Control) shows each turn live: app messages, inbox turns and the question card. A phone answer to the card reaches the session | FIG and KIWI answered on the phone |
| S11 | ✅ An idle app session process stayed alive for 3 h 45 min. Then it ended cleanly: `session.end` fired and the lease became `none`. The supervisor must expect this and use the SDK or a peer message to wake the session | S2: last turn 16:04, end 19:49:33 |

### Decision modes for the orchestrator

| Mode | Use | Turn |
|---|---|---|
| `ask_user` with `waitSeconds` > 0 | Short decisions, a quick answer is likely | Stays open; the answer is the tool result |
| `ask_user` with `waitSeconds` 0 | Long decisions | Ends; the answer wakes the session through the inbox |
| `AskUserQuestion` redirect | A model that uses the built-in tool | Waits up to 300 s for the store; then the app card |

## P1 result (2026-11-15)

The mod `mod/orch` replaces `mod/orch-spike` in `CLAUDE_CODE_PLUGIN_DIRS`. The store contract is in `docs/store.md`.

| Part | Result | Evidence |
|---|---|---|
| `report` tool, lease, filtered mirror | ✅ In `claude -p` and in the app | Session `989d112c`, `report.json` |
| Inbox turn in an idle app session | ✅ | Session `ff9d65e6`, `inbox/1.txt` to `4.txt` |
| `ask_user` pending, answer by inbox | ✅ | T1: LIME |
| `ask_user` blocking, answer in time | ✅ | T2: CASHEW after 24 s |
| `ask_user` blocking, timeout, then answer by inbox | ✅ The mode changes to `pending` | T4: FLAX |
| `AskUserQuestion` from the store | ✅ | T3: ACAI |
| `AskUserQuestion` with no answer | ✅ The app card shows after 300 s | Session `989d112c` |
| Hot reload | ✅ The mod loads the cursor and the state from the store | `activate ... cursor=3.txt` |
| `/orch-enable` command | Written, not tested in the app | |
| One branch | ✅ One leaf after all tests | `p1-app.sh` |

Test script: `spikes/p1-app.sh` (it reads the session id from `spikes/p1.id`).

### Findings

1. After the user presses Stop, the engine refuses each plugin submit until the person types. A message from another session does not clear the Stop. The mod keeps the inbox file and the answer, tries again every 3 s, and writes `blocked.json`. The supervisor must show this state to the user, and must not use a peer message to go around the Stop.
2. `$.prompt.submit` resolves when its turn starts. The mod submits only when the session is idle, and the heartbeat has its own timer.
3. `tsc` gives TS2589 (type instantiation too deep) on `on('tool.call', { tool: 'mcp__...' })`. The spike mod has the same error and loads correctly. This is a limit of the type checker.

## P2 result (2026-11-15)

The supervisor is in `supervisor/` (Node 26 runs the TypeScript files directly). Start: `npm start` in `supervisor/`. The store contract and the HTTP API are in `docs/store.md`.

| Part | Result | Evidence |
|---|---|---|
| Unit tests (fake runner, temporary store) | ✅ 9 tests | `npm test` |
| Session tree: root + 2 children with `spawn_child`, a decision through the API, reports to the parent | ✅ About 90 s from start to the root report `done` | Root `b5f12905`, children `249fb7b0` and `48d13d06` |
| Resume of idle sessions: inbox, answers and child reports go to a session that is not live | ✅ | `store/supervisor/log.txt` |
| Hand-off supervisor → app: the session opens in the app, then a supervisor message goes through the mod | ✅ After the fix, one leaf | `249fb7b0` |

### Findings

1. Lease race (fixed). When an app process opened a session, the heartbeat file still had the old time of the SDK run. The supervisor released the new lease before the first beat and started an SDK turn, so the transcript got two leaves (`48d13d06`). Fix: the mod writes the heartbeat at activation and takes back a released lease; a lease younger than 15 s counts as live.
2. Two processes that write one log file lose lines. The mod now writes `log-<holder>.txt`.
3. In an SDK session, the `AskUserQuestion` fallback has no app card. Briefs must tell sessions to use `ask_user`.

### Open for later phases

- P3: the decision policy (risk, timeout, default) uses `risk` and `defaultOption` of each decision.
- A session that the supervisor starts is not in the app sidebar. To show it, open it with `claude --desktop --resume <id>`.
- The supervisor does not restart a failed turn after `maxFailures` (3) until it restarts.

## Change: no click questions (2026-11-15)

The user answers by voice and in free text, so click questions (A, B, C or other) are not used. The `AskUserQuestion` redirect from S8 is removed. A deny rule in the user settings removes the tool, and the mod refuses it as a fallback. Questions go to the reply text (normal sessions) or to `ask_user` (orchestrator sessions). P3 and P4 must show free-text answers, with the options as suggestions only.

## P3 result (2026-11-15)

The policy is configurable (`config/policy.default.json`, copied to `store/policy.json`): any number of levels, a definition and a timeout for each level, and a docs check for each level. See `docs/store.md`.

| Part | Result | Evidence |
|---|---|---|
| Unit tests | ✅ 16 tests (7 new): timeout, no default for `null` or no `defaultOption`, unknown level, docs answer, docs miss then timeout, no docs check for a level without it, review | `npm test` |
| Docs answer | ✅ A `low` question that `AGENTS.md` answers got the answer in about 20 s, with the source | Test repo: LIME from "AGENTS.md, Rules" |
| Default after timeout, stacked branch | ✅ A `medium` question got its default after 30 s. The session put the work on `orch/decision-<id>` on top of `main` | Test repo: BLUE |
| Review: refuse | ✅ The session went back to `main`, did the work again with the direction of the user, and kept the refused branch | Test repo: RED |
| Ledger | ✅ One line for the docs answer and one for the default, then one for the review | `store/ledger.jsonl` |

### PR stack test (2026-11-15)

A supervisor session on a private GitHub test repository (`MgCohen/orch-pr-test`). Three `medium` questions, timeout 30 s.

| Step | Result |
|---|---|
| Three defaults | Pass. Three draft PRs: `main` ← #1 ← #2 ← #3, each base the branch below it |
| Approve #1 | Pass. #1 is ready for review, not merged |
| Change #2 ("use SMALL") | Pass. A new commit on #2. #3 is rebased on it and pushed with `--force-with-lease` |
| Refuse #2 | Pass. #2 and #3 are closed with a comment, and their branches stay. New branches `…-v2` start from #1: #4 has the direction of the user, #5 reuses the BLUE commit of #3 |
| Ledger | Pass. One line for each default and each review |

Findings:

- Two reviews that arrive 2 s apart run as two turns, because a POST starts a tick at once. This is correct.
- The rebuilt PRs (#4, #5) use the branches `orch/decision-<same id>-v2` and name the old decision id in their bodies. But `reviewText` names the closed branch `orch/decision-<id>`, not the `-v2` branch. Also, decision #2 already has a refuse review, so a second review of the same id is not clear. #5 holds a default (BLUE) that nobody reviewed. Fix in P4: a rebuild makes a new decision version (`<id>-v2`) in the store, the refuse text names that suffix, and `reviewText` uses the newest version.
- The body of #4 still has the old question ("Which size do we use in size.txt?"), and its title has no "(default decision)". The rebuild must write a new body.
- Each PR comment has the author `MgCohen`, because the session uses the gh token of the user. Fix later: each orchestrator text starts with a fixed tag, for example `[orch]`.
- The `mode` of a decision stays `pending` after an answer. This is correct: `mode` is how the session asked, and `answers/` holds the state.

PR format that the session used (the GitHub test repository is deleted):

- Default PR title: `Set shape to SQUARE (default decision)`.
- Default PR body: one sentence, then `Question: …`, `Default answer: SQUARE (no answer from the user in 30 s)` and `Decision id: toolu_…`. A stacked PR adds its base PR. Each body ends with "Approve or refuse this decision alone."
- Close comment of the refused PR: "Closed: the user refused this decision. … The branch stays. The new work continues from the shape branch."
- Close comment of a PR above it: "Closed: this PR is stacked on PR #2, and the user refused the size decision of PR #2. The branch stays."
- Rebuilt PR body: "It replaces PR #2, which is closed." #5 adds "reuses the commit of PR #3" and "Not reviewed yet."
- The session saw a conflict between the direction of the user and `AGENTS.md`. It followed the user and wrote a comment on the PR. This is the correct order.
