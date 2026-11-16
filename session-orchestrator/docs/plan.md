# Session orchestrator: implementation plan

Design page: https://claude.ai/artifact/EggEYH4ivUuPGG9Pw4k1y1 (source `docs/orchestrator-design.html`).

## Goal

One local system controls all Claude Code sessions of the user:

- It starts, nests, watches, stops and archives sessions.
- It delivers each message. No message gets lost.
- It keeps one flat queue of decisions for the user, with the path of each decision.
- It applies a default decision after a timeout, by risk level. It records the decision and puts its work on a stacked draft PR (`onDefault`: `stacked-pr`, `commit` or `none`).
- A phone UI shows the queue (swipe cards) and the state of each session.

The main problem: in nested sessions, decisions get lost in the middle of the tree, and the user does not see them. The tool is local and personal, not a product. It runs on the subscription of the user through the Agent SDK and `claude -p`, with no API key.

## Main rule: one writer for each session (the lease)

The spike `Spike: two writers` showed this fact. If the app process and a CLI process write to one session, the transcript gets two branches, and each writer sees only its own branch.

| Lease holder | Who sends a turn | How |
|---|---|---|
| `live` | The process that has the session open (desktop app, phone through Remote Control, terminal) | The supervisor writes to the inbox. The mod in that process submits the turn |
| `supervisor` | The supervisor | Agent SDK resume of the session id |
| `none` | Nobody | The next writer takes the lease |

A hand-off only moves the lease. The transcript stays one branch.

## Parts

The folders today:

```
session-orchestrator/
├─ mod/orch/       Claude Code mod; loads in each session; inert unless the session is enabled
│   inbox → $.prompt.submit, mirror rows, heartbeat, status, lease, tools (ask_user, report, spawn_child)
├─ supervisor/     Node process, Agent SDK
│   store, scheduler, heartbeat check, decision policy, SDK runner, HTTP API
├─ config/         policy.default.json, policy.test.json
├─ spikes/         spike scripts and the test briefs (p2, p3, p4)
└─ store/          runtime files, ignored by git: sessions/<id>/..., ledger.jsonl, policy.json
```

The target folders (milestone M0). The design page, tab Code, gives the reasons and the package graph:

```
session-orchestrator/
├─ packages/
│  ├─ core/        store contract: types, file names, order, texts. No file IO
│  ├─ store/       file IO of the contract (atomic write, read, list)
│  ├─ supervisor/  tick, lease, policy, SDK runner, HTTP API
│  ├─ cli/         the orch command for the user, scripts and sessions
│  └─ ui/          phone web page, served by the supervisor
├─ plugin/         the Claude Code plugin that each session loads
│  ├─ hooks/       register.ts + inbox, lease, mirror, tools (one file each)
│  ├─ skills/      orch-ask, orch-stack, orch-brief
│  ├─ agents/      orchestrator, worker, docs-checker
│  └─ commands/    orch-enable
├─ prompts/        text templates: default note, review texts, briefs
├─ config/         policy.default.json, policy.test.json
├─ tests/          unit, contract, e2e
├─ docs/
├─ spikes/         frozen
└─ store/          runtime, ignored by git
```

The `orch` command (M1) does each read and write of the store, for the user, the scripts and the sessions: `new`, `send`, `tree`, `status`, `decisions`, `answer`, `review`, `ledger`, `decision start`, `decision pr`, `stack rebase`, `stack close`, `stop`, `archive`, `open`, `doctor`. The stack commands replace the 5 steps of the default note.

## Phases

| Phase | Work | Done when |
|---|---|---|
| P0 | Spikes S0 to S11 (below) | Each spike has a result in this file |
| P1 | Mod: inbox, mirror, heartbeat, lease, `ask_user`, `report` | A desktop session gets inbox turns and its rows are in the store |
| P2 | Supervisor: store, SDK runner, lease rules, heartbeat check, `spawn_child` | The supervisor starts a session tree and resumes idle sessions |
| P3 | Decision policy: risk levels, timeouts, default, record, commit or tag | An unanswered low-risk decision gets its default and a record |

After P3, the work continues in milestones to the stable version 1.0:

| Milestone | Work | Done when |
|---|---|---|
| M0 | Packages, `core`, prompts as files, plugin split | The same 16 tests pass, with no behavior change |
| M1 | `orch` command, stack commands, decision versions, new PR bodies, `[orch]` tag | The PR stack test runs on `orch` commands |
| M2 | Agents, skills and commands in the plugin | Briefs refer to skills, not long texts |
| M3 | Stop, archive, worktrees, restart after failures, app import | Two code sessions run in one repository |
| M4 | Phone UI, tunnel, sign-in | You answer and review from the phone |
| M5 | One vsa-template stream under the orchestrator | Start to merge with no lost decision |
| M6 | End-to-end script, crash recovery, store version | A supervisor restart loses nothing |

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
| `AskUserQuestion` redirect | Removed (no click questions) | None |

## P1 result (2026-11-15)

The mod `mod/orch` replaces `mod/orch-spike` in `CLAUDE_CODE_PLUGIN_DIRS`. The store contract is in `docs/store.md`.

| Part | Result | Evidence |
|---|---|---|
| `report` tool, lease, filtered mirror | ✅ In `claude -p` and in the app | Session `989d112c`, `report.json` |
| Inbox turn in an idle app session | ✅ | Session `ff9d65e6`, `inbox/1.txt` to `4.txt` |
| `ask_user` pending, answer by inbox | ✅ | T1: LIME |
| `ask_user` blocking, answer in time | ✅ | T2: CASHEW after 24 s |
| `ask_user` blocking, timeout, then answer by inbox | ✅ The mode changes to `pending` | T4: FLAX |
| `AskUserQuestion` from the store (removed later) | ✅ | T3: ACAI |
| `AskUserQuestion` with no answer (removed later) | ✅ The app card shows after 300 s | Session `989d112c` |
| Hot reload | ✅ The mod loads the cursor and the state from the store | `activate ... cursor=3.txt` |
| `/orch-enable` command | Written, not tested in the app | |
| One branch | ✅ One leaf after all tests | `p1-app.sh` |

Test script: `spikes/p1-app.sh` (it reads the session id from `spikes/p1.id`).

### Findings

1. After the user presses Stop, the engine refuses each plugin submit until the person types. A message from another session does not clear the Stop. The mod keeps the inbox file and the answer, tries again every 3 s, and writes `blocked.json`. The supervisor must show this state to the user, and must not use a peer message to go around the Stop.
2. `$.prompt.submit` resolves when its turn starts. The mod submits only when the session is idle, and the heartbeat has its own timer.
3. `tsc` gives TS2589 (type instantiation too deep) on `on('tool.call', { tool: 'mcp__...' })`. The spike mod has the same error and loads correctly. This is a limit of the type checker.

## P2 result (2026-11-15)

The supervisor is in `supervisor/` (Node 26 runs the TypeScript files directly). Start: `node src/main.ts` in `supervisor/`. The store contract and the HTTP API are in `docs/store.md`.

| Part | Result | Evidence |
|---|---|---|
| Unit tests (fake runner, temporary store) | ✅ 9 tests | `node --test test/` |
| Session tree: root + 2 children with `spawn_child`, a decision through the API, reports to the parent | ✅ About 90 s from start to the root report `done` | Root `b5f12905`, children `249fb7b0` and `48d13d06` |
| Resume of idle sessions: inbox, answers and child reports go to a session that is not live | ✅ | `store/supervisor/log.txt` |
| Hand-off supervisor → app: the session opens in the app, then a supervisor message goes through the mod | ✅ After the fix, one leaf | `249fb7b0` |

### Findings

1. Lease race (fixed). When an app process opened a session, the heartbeat file still had the old time of the SDK run. The supervisor released the new lease before the first beat and started an SDK turn, so the transcript got two leaves (`48d13d06`). Fix: the mod writes the heartbeat at activation and takes back a released lease; a lease younger than 15 s counts as live.
2. Two processes that write one log file lose lines. The mod now writes `log-<holder>.txt`.
3. In an SDK session, the `AskUserQuestion` fallback has no app card. Briefs must tell sessions to use `ask_user`.

### Open for later phases

- P3: the decision policy (risk, timeout, default) uses `risk` and `defaultOption` of each decision.
- A session that the supervisor starts is not in the app sidebar. To show it, open it with `claude.exe --desktop --resume <id>` through PowerShell `Start-Process` (see `docs/know-how.md`).
- The supervisor does not restart a failed turn after `maxFailures` (3) until it restarts.

## Change: no click questions (2026-11-15)

The user answers by voice and in free text, so click questions (A, B, C or other) are not used. The `AskUserQuestion` redirect from S8 is removed. A deny rule in the user settings removes the tool, and the mod refuses it as a fallback. Questions go to the reply text (normal sessions) or to `ask_user` (orchestrator sessions). The decision cards (M4) must show free-text answers, with the options as suggestions only.

## P3 result (2026-11-15)

The policy is configurable (`config/policy.default.json`, copied to `store/policy.json`): any number of levels, a definition and a timeout for each level, and a docs check for each level. See `docs/store.md`.

| Part | Result | Evidence |
|---|---|---|
| Unit tests | ✅ 16 tests (7 new): timeout, no default for `null` or no `defaultOption`, unknown level, docs answer, docs miss then timeout, no docs check for a level without it, review | `node --test test/` |
| Docs answer | ✅ A `low` question that `AGENTS.md` answers got the answer in about 20 s, with the source | Test repo: LIME from "AGENTS.md, Rules" |
| Default after timeout, stacked branch | ✅ A `medium` question got its default after 30 s. The session put the work on `orch/decision-<id>` on top of `main` | Test repo: BLUE |
| Review: refuse | ✅ The session went back to `main`, did the work again with the direction of the user, and kept the refused branch | Test repo: RED |
| Ledger | ✅ One line for the docs answer and one for the default, then one for the review | `store/ledger.jsonl` |

### PR stack test (2026-11-15)

A supervisor session on a private GitHub test repository (`MgCohen/orch-pr-test`). Three `medium` questions, timeout 30 s. In this test, "the user" is the test script: the reviews and their texts came from `curl` calls, not from the real user.

| Step | Result |
|---|---|
| Three defaults | Pass. Three draft PRs: `main` ← #1 ← #2 ← #3, each base the branch below it |
| Approve #1 | Pass. #1 is ready for review, not merged |
| Change #2 ("use SMALL") | Pass. A new commit on #2. #3 is rebased on it and pushed with `--force-with-lease` |
| Refuse #2 | Pass. #2 and #3 are closed with a comment, and their branches stay. New branches `…-v2` start from #1: #4 has the direction of the user, #5 reuses the BLUE commit of #3 |
| Ledger | Pass. One line for each default and each review |

Findings:

- Two reviews that arrive 2 s apart run as two turns, because a POST starts a tick at once. This is correct.
- The rebuilt PRs (#4, #5) use the branches `orch/decision-<same id>-v2` and name the old decision id in their bodies. But `reviewText` names the closed branch `orch/decision-<id>`, not the `-v2` branch. Also, decision #2 already has a refuse review, so a second review of the same id is not clear. #5 holds a default (BLUE) that nobody reviewed. Fix in M1: a rebuild makes a new decision version (`<id>-v2`) in the store, the refuse text names that suffix, and `reviewText` uses the newest version.
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

## Next steps

Status: the user reviewed the design page and accepted it. The user then asked for the folder structure, the code topology and a roadmap to a stable version. They are on the page (tabs Code and Roadmap) and in this file. The next work is M0, after the user accepts the roadmap. The notes arrive in the chat, or as comments on the page: read them with the `ArtifactComments` tool on the page URL. After a design change, edit `docs/orchestrator-design.html` and publish it with the `Artifact` tool and `url` set to the page URL.

### Agreed details of M1 and M4

- M1: a rebuild after a refuse makes `<id>-v2` in the store, the refuse text names the `-v2` branch, and `reviewText` uses the newest version. A rebuilt PR gets a new title and body.
- M4: the session tree and the decision cards. Each card has a free-text field and voice input. The options show as suggestions only. A default decision has the buttons approve, change and refuse.

### M5 scope

Run one vsa-template stream under the orchestrator from start to merge. The supervisor must copy these habits of the manual orchestrator session:

- A status check every 5 minutes. Examine each session yourself, because messages can get lost.
- If a question has no answer after 2 checks, decide it with the recommendation, and report it later.
- Archive a child session that you started when it is done and nobody needs it. Keep it while its PR is open. Put this rule in each child brief.
- Never archive a session that you did not start (examine its `startedBy`), unless the user names it. Never archive a session that waits for the user.
- One decision group for each session. The title names all of its scope.
- If an archive is refused, turn off Remote Control, then rename the session with `[archive/done]`.
- Detach exploration sessions, and relay their results only when the user asks.

Rules of the vsa-template work that the M5 stream must obey:

- The orchestrator may review and merge PRs, except PR #8 (rules and analyzers).
- Run CI only at the end of a stream, one manual run (no CI on each PR before 0.1.0).
- Run only the tests that a change can affect. The full suite runs in CI.

### Open questions

| Question | Recommendation |
|---|---|
| Language of the `orch` command | Node 26, the same as the supervisor, no build step |
| Package layout | npm workspaces in one `package.json` |
| Can a plugin hook import a relative `.ts` file? | Spike at the M0 start. If not, keep one file and generate it |
| Does `CLAUDE_CODE_PLUGIN_DIRS` give the plugin skills and agents to a session? | Spike at the M2 start |
| Worktrees: does the supervisor make them, or each brief? | The supervisor makes them and records the folder in `meta.json` |
| Tunnel: which tool and which sign-in? | Cloudflare Tunnel with Cloudflare Access |
| Store: files, or SQLite? | Files until M5 shows a real problem |
| `medium` has `checkDocs: true`. The user named the docs answer only for `low` | Keep it, and ask the user at the M4 start |
| Delete the old folder `Projects/claude-orchestrator`? It also holds a stray `session-orchestrator/` subfolder with copied type files | Yes, after the next app restart, when the user says so |
| UI stack: plain HTML from `http.ts`, or a separate app? | Plain HTML and JS served by `http.ts`, no build step |
| Sign-in for the API (it listens on 127.0.0.1 with no auth) | Cloudflare Access in front of the tunnel, plus a token header |
| Swipe meaning on a decision card | Right: accept the default. Left: open the free-text answer |
| Voice input: browser speech or phone dictation? | Phone dictation in the text field: no extra code |
| Which vsa-template stream runs first in M5? | Ask the user at the M5 start. Use the stable roadmap of vsa-template |
| Does a Cloudflare account with a domain exist for the tunnel? | Ask the user at the M4 start. A Cloudflare connector exists in the sessions |

### Known gaps

- PR comments show the user (`MgCohen`) as author. Start each orchestrator text with `[orch]`.
- Supervisor sessions are not in the app sidebar. Open them with the app import (`docs/know-how.md`).
- The supervisor does not restart a session after `maxFailures` (3) until the supervisor restarts.
- `/orch-enable` is not tested in the app.
- The supervisor has no API to stop or archive a session, but the goal needs both.

## Test sessions in the app sidebar

None of these is archived. Archive them only when the user says so.

| Title | Session | State |
|---|---|---|
| Spike: two writers | `dee17212` | Split transcript (S0) |
| Spike S1 cli | `f5034f63` | Done |
| Spike S2: inbox | `6ad6c434` | Remote Control on |
| Spike S8: ask | `3df803ac` | Remote Control on |
| Test P1: orch mod | `989d112c` | In the Stop state |
| Test P1: orch mod 2 | `ff9d65e6` | Done |
| Test P2: child A hand-off | `249fb7b0` | Done |
| Test P2: child B hand-off | `48d13d06` | Split transcript (lease race) |

The store folders of the P2 sessions (`b5f12905`, `249fb7b0`, `48d13d06`) are in `store/old-spikes/`, so the supervisor does not watch them. The build session of this project is `15fac584` (a vsa-template worktree).
