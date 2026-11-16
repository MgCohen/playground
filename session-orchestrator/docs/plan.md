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
├─ src/            the C# supervisor: Core, Store, Supervisor, Web (see the target folders below)
├─ tests/          NUnit tests
├─ config/         policy.default.json, policy.test.json
├─ spikes/         spike scripts, the test briefs (p2 to p5), m0-cli, supervisor-ts (frozen)
└─ store/          runtime files, ignored by git: sessions/<id>/..., events/, bodies/, ledger.jsonl, policy.json
```

The stack is C# on .NET 10 and the `claude` CLI. The user uses C# for all projects. The UI is Blazor. Only the plugin hooks stay in TypeScript, because the Claude Code engine runs them, and the Blazor UI can have a little JavaScript. The supervisor runs session turns with `claude -p` (`--session-id` for a new session, `--resume` for the next turn, `--output-format stream-json`), because the Agent SDK has no C# version.

The target folders. The design page, tab Build, gives the full tree with the detail of each line, and the project graph:

```
session-orchestrator/
├─ Orchestrator.slnx, global.json, Directory.Build.props, Directory.Packages.props
├─ src/
│  ├─ Orchestrator.Core/        contract records, order, lease, policy, templates, events. No IO
│  ├─ Orchestrator.Store/       file IO: atomic write, read, event stream
│  ├─ Orchestrator.Supervisor/  tick, policy runner, claude -p runner
│  ├─ Orchestrator.Web/         one process: hosts the supervisor, HTTP API, Blazor UI and cards
│  └─ Orchestrator.Cli/         the orch .NET tool
├─ plugin/                      the Claude Code plugin (thin TypeScript hooks, skills, agents, commands)
├─ templates/                   one folder for each message kind: schema.json, render.md, example.json
├─ config/                      policy.default.json, policy.test.json
├─ tests/Orchestrator.Tests/    NUnit: unit, template, contract
├─ tests/e2e/                   the live test as one script
├─ docs/
├─ spikes/                      frozen TypeScript and JavaScript, plus the TypeScript supervisor after the port
└─ store/                       runtime, ignored by git
```

Each message is a filled template. A sender gives the fields, `Orchestrator.Core` validates them against `schema.json`, a session gets the text of `render.md`, and the Blazor UI draws the card of the template. The input schema of each plugin tool is the same `schema.json`. The design page, tab Messages, gives the fields of each template.

| Template | From → to | Sent with |
|---|---|---|
| `decision` | Session → user | `ask_user`: question, context, snippet, options, recommendation, risk, defaultOption |
| `answer` | User, docs, policy → session | UI, `orch answer`, supervisor: text, by, source, note |
| `review` | User → session | UI, `orch review`: verdict, text |
| `report` | Session → parent, user | `report`: state (done, blocked), summary, done, next, blockers, links |
| `update` | Session → user | `update`: step, progress, summary. No action needed |
| `brief` | User, parent → new session | `spawn_child`, `orch new`: title, goal, scope, doneWhen, skills, agent, cwd, worktree |
| `notice` | Supervisor → session | default, review, child-report, blocked |

Each template message, and each change of state, is also an event in one append-only stream, `store/events/<day>/<id>.json`. A reader picks what it needs with a query: the UI queue takes `decision` events, the timeline takes all events of the last hours, and the ledger of today becomes the query `by = docs or policy, or kind = review`. The event envelope is small: `id`, `at`, `session`, `kind`, `from`, `to` (full session ids), `subject` (one line), `body` (the path of the body file with the template fields), `refs`. Rules: one new file for each event, so two writers never write one file; never change or delete an event; `Orchestrator.Store` writes the event first, then the state file. The design page, tab Messages, gives the event kinds and the queries.

The `orch` command (M1), a .NET tool, does each read and write of the store, for the user, the scripts and the sessions: `new`, `send`, `tree`, `status`, `decisions`, `answer`, `review`, `events`, `decision start`, `decision pr`, `stack rebase`, `stack close`, `stop`, `archive`, `open`, `template`, `doctor`. The stack commands replace the 5 steps of the default note.

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
| M0 | Port to .NET: Core, Store, Supervisor, Web API, `claude -p` runner | The 16 tests pass in NUnit, and the PR stack live test passes |
| M1 | Templates, event stream, `orch` command, stack commands, decision versions, new PR bodies, `[orch]` tag | The PR stack test runs on `orch` commands |
| M2 | Thin plugin: tool schemas from the templates, skills, agents, commands | Briefs refer to skills, not long texts |
| M3 | Stop, archive, worktrees, restart after failures, app import | Two code sessions run in one repository |
| M4 | Blazor UI (tree, cards, timeline), tunnel, sign-in | You answer and review from the phone |
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

## Event stream spike (2026-11-16)

The TypeScript supervisor and the mod write one event file for each message, in each direction, to `store/events/<day>/<id>.json`. `GET /api/events` returns the stream. The first version had the full payload in `data`. The user asked for small events, so each event now has `subject` (one line) and `body` (the path of `store/bodies/<day>/<id>.json`), and `from` and `to` name the full session id. `spikes/p5/events.json` is the first run. `spikes/p5/stream/` is the same run in the new shape (made by `convert.mjs`), and `node timeline.mjs stream` prints it with the session titles.

Test: a root session starts a child, the child asks the user a `high` question, the user answers "GREEN" (not one of the options), the child reports to the root, the root reports done, then the user sends a question to the root. Result: 38 events in 149 s, and each direction is in the stream.

| Direction | Event kinds |
|---|---|
| User → session | `brief`, `message`, `answer`, `review` |
| Session → user | `decision`, `report` (root), `session.output` (live holder) |
| Parent → child | `brief` |
| Child → parent | `report`, `report.forward` |
| Supervisor → session | `turn.start` with the prompt; `answer` by `policy` or `docs` |
| Session → supervisor | `session.output`, `turn.end` with the last text |
| Received by a session | `session.input`: each prompt that the session got, from any sender |

Findings:

- A free-text answer that is not one of the options works: the child used GREEN and told the user.
- `session.output` has empty rows (thinking blocks with no text). Leave them out.
- `session.input` has the origin `unclassified` in an SDK turn. Use the `turn.start` before it to name the sender.
- Tool calls show as `[tool <name>]` in `session.output`. Make them a separate kind, `session.tool`, so a filter can hide them.
- Some events repeat one fact from two sides (`report` and `report.forward`, the last `session.output` and `turn.end`). Keep both, and link them with `refs`, because each side is a different direction.
- The supervisor and the mod write the new shape (16 tests pass, the mod validates). A new live run is not done: the converted sample shows the shape.
- The `orch events` query and the C# port are still to do (M1).

## M0 result (2026-11-16)

The supervisor runs in C#. `spikes/supervisor-ts/` keeps the TypeScript version, frozen. The decisions of the build are in `decisions.md`.

| Part | Result | Evidence |
|---|---|---|
| Spike: `claude -p` from C# | ✅ Fixed session id, resume, stream-json lines, mod loaded | `spikes/m0-cli`: turn 2 knew KIWI |
| NUnit tests | ✅ The 16 tests of the TypeScript supervisor, ported | `dotnet test --solution Orchestrator.slnx` |
| PR stack live test | ✅ Three defaults gave the stack #1 ← #2 ← #3. Approve made #1 ready. Change rebased #3 on #2. Refuse closed #2 and #3 and made #4 and #5 on `-v2` branches | Test repository `MgCohen/orch-m0-stack-test` (deleted), session `67c78a43` |
| Event stream | ✅ 69 events, each direction | `GET /api/events` of the test store |

Findings:

- The TypeScript findings of the PR stack test are still true in C#: the rebuilt PR keeps the old title, and the session writes the PR text by itself. M1 fixes them with the stack commands and the PR templates.
- The session saw a conflict between the direction of the user and `AGENTS.md` again, followed the user, and wrote it in the PR body.
- `session.output` still has empty rows and `session.input` has the origin `unclassified`. M1 fixes them in the mod.

## M1 result (2026-11-16)

| Part | Result | Evidence |
|---|---|---|
| Templates | ✅ Seven kinds in `templates/`. Each example matches its schema, and each render fills its places | `TemplateTests` |
| Session texts | ✅ The defaults, reviews, docs notes and child reports come from `templates/notice/render/` | `PolicyTests` |
| `orch` command | ✅ `new`, `send`, `tree`, `status`, `decisions`, `answer`, `review`, `events`, `decision start/pr/ready`, `stack rebase/close`, `template`, `doctor` | `CliTests` with a local git repository |
| PR stack test on `orch` | ✅ The session used `orch decision start` and `orch decision pr` for each default, `orch decision ready` for approve, `orch stack rebase` for change and `orch stack close` for refuse. The user part used `orch review` | Test repository `MgCohen/orch-m1-stack-test` (deleted), session `a1b09966`, 89 events |
| Decision versions | ✅ The refuse planned `-v2` of the refused decision and of the decision above it. A review after it named the `-v2` branch and wrote `reviews/<id>-v2.json` | Rebuilt PRs #4 and #5 |
| PR texts | ✅ Each title starts with `[orch]` and names its origin: "default decision" or "direction of the user". The body of a rebuilt PR has the new answer, its version and "It replaces PR #2" | PRs #1 to #5 |
| Event fixes, supervisor side | ✅ Each inbox file names its sender. `turn.json` gives the senders of a turn. `report.forward` links `refs.report`, `turn.end` links `refs.output` | `Supervisor.cs` |
| Event fixes, mod side | ❌ Blocked: auto mode could not evaluate edits to `mod/orch/hooks/register.ts`. The fixes go into the plugin of M2 | `decisions.md` |
| Unit tests | ✅ 41 tests | `dotnet test --solution Orchestrator.slnx` |

Findings:

- A supervisor that runs from `src/Orchestrator.Web/bin/` locks its DLLs, so a build fails while it runs. Run live tests from a published copy in the scratchpad.
- The session followed each `orch` instruction with no extra text in the brief. The brief of the test was the same as in P3.
- The title of a rebuilt PR cuts a long direction of the user at 60 characters.

## M2 result (2026-11-16)

| Part | Result | Evidence |
|---|---|---|
| Spike: relative import in a hook | ✅ | `helper.ts` ran at `session.start` |
| Spike: plugin skills and agents | ✅ Through `--plugin-dir` and `CLAUDE_CODE_PLUGIN_DIRS`, with `orch:` names | A session listed `orch:orch-ask` to `orch:orch-stack` and `orch:worker` |
| Thin plugin | ✅ Five hook modules. Each tool takes its schema from its template. `spawn_child` runs `orch new` | `plugin/`, `ContractTests` |
| Event fixes in the plugin | ✅ No empty `session.output`. Tool calls are `session.tool`. Each `session.input` names its sender. `report.forward` and `turn.end` link their pair in `refs` | Session `19d1b782` and its child `99961e91` |
| Short brief with skills | ✅ A five-line brief that names `orch-ask`, `orch-stack` and `orch-report` gave three stacked default PRs | Session `5bec7d79`, test repository deleted |
| Parent and child with agents | ✅ The `orchestrator` root started a `worker` child. The user answered GREEN (not an option), and both reports named GREEN | `summary.txt` = GREEN |
| Settings | ✅ `CLAUDE_CODE_PLUGIN_DIRS` points to `plugin/` | `settings.json.bak-m2-plugin` |
| Unit tests | ✅ 46 tests | `dotnet test --solution Orchestrator.slnx` |

Findings:

- A session loads the skills of its brief with the `Skill` tool before its first step. The events show each load as a `session.tool` event.
- The test supervisor ran the plugin with `--plugin-dir` and `--permission-mode auto`, so the settings stayed unchanged until the end of the test.

## M3 result (2026-11-16)

| Part | Result | Evidence |
|---|---|---|
| Two code sessions in one repository | ✅ "Calc: subtract" and "Calc: multiply" changed `calc.py` at the same time, each in its own worktree and branch. `main` stayed clean | Test repository `m3repo`, sessions `5317be82` and `5e999bf0` |
| Worktrees | ✅ The first turn of a brief with `worktree: true` makes `<repo>.wt/orch-<id8>` on `orch/session-<id8>` | `branch.create` events |
| Stop | ✅ `orch stop` ended a running turn after 67 s. The `claude` process ended, the inbox stayed, and no new turn started | Session `f79426d3` |
| Archive | ✅ `orch archive` removed the clean worktrees and kept the branches. It refuses open decisions and open PRs without `--force` | `LifecycleTests` |
| Restart after failures | ✅ After `maxFailures`, the session waits 5 min (then 10, 20...), gets the `blocked` notice and runs again | `LifecycleTests` |
| App import | ✅ `orch open` runs `claude.exe --desktop --resume <id>` with `Start-Process`, and refuses a session with a running turn. Not run live: it opens the app of the user | `orch open --dry-run` |
| Unit tests | ✅ 51 tests | `dotnet test --solution Orchestrator.slnx` |

Findings:

- A stop ends the `claude` process tree, but a grandchild of the session's Bash tool can live on: the `powershell Start-Sleep` of the test ran to its end. That process kept the worktree folder locked, so `orch archive` removed the worktree from git but not the folder. A Windows job object did not help: the grandchild left the job, probably because the host job allows silent breakaway. Archive a stopped session after its tools end.
- `git worktree add` works from a session folder of any depth: the supervisor finds the repository with `git rev-parse --show-toplevel`.

## M4 result (2026-11-16)

| Part | Result | Evidence |
|---|---|---|
| Queue view | ✅ Each open decision is a card: risk, session, question, context, recommendation, options as suggestions, a free-text field and "Accept default". Each default to review has approve, change and refuse | Phone test, `WebTests` |
| Answer from the phone view | ✅ The free-text answer "GREEN, like the logo" (not an option) reached the session, which wrote GREEN | Session `47991b46` |
| Review from the phone view | ✅ "Change: use SMALL" on a default reached the session, which changed the decision branch to SMALL | Session `29e75a65` |
| Sessions view | ✅ The tree with state, lease, report, update, worktree, a message field, stop, resume and archive | Phone test |
| Timeline view | ✅ The stream, newest first, with filters for time, kind group, session and tool calls. Each template event opens its fields | Phone test |
| Sign-in | ✅ `ORCH_TOKEN`: a token in the link once (then a cookie), or the header `X-Orch-Token` for the API | `WebTests` |
| Tunnel | ❌ Blocked: Cloudflare Tunnel and Access need an account of the user | `decisions.md` |
| Unit tests | ✅ 56 tests | `dotnet test --solution Orchestrator.slnx` |

The phone test used the browser pane of the desktop app at 375 × 812, not a real phone.

Findings:

- A view reloads every 2 s. An open `<details>` card stays open across a reload, because each card has a `@key`.
- A review notice arrives at the session as `session.input` from `supervisor`, because the supervisor writes the review text. The `review` event before it names the user.

## M5 result (2026-11-16)

A real vsa-template stream ran under the orchestrator on a private copy, `MgCohen/orch-m5-vsa-copy`: issue #140, "a validator is only in the slice of its handler". The run on the real repository waits for the user (`decisions.md`).

| Step | Result |
|---|---|
| Root | An `orchestrator` session (`fddb233c`) started one `worker` child with a worktree |
| Child | `91089ede` asked two design questions (medium). The policy applied both defaults after 120 s. Each default got its own draft PR (#1, #2). The work went to PR #3 to `main` |
| Review and merge | The root read the diff, ran Architecture.Tests (164 passed) and Engine.Tests (22 passed), and merged PR #3. No CI, no full suite |
| Report | The root reported done, with each default to review and the child kept open for its PRs |
| Decisions | 2 of 2 have an answer and a delivery mark. No decision was lost. 106 events |

Findings:

- PR #3 had the top decision branch as its head, so its merge took the commits of the two unreviewed defaults. Fix (user review, 2026-11-16): `orch merge` refuses such a merge in code, and a hook blocks `gh pr merge` in orchestrator sessions.
- The root did not archive the child, because the child has open PRs. This follows the archive rule.
- The default option of a decision can be a long text. The PR title cuts it at 60 characters.

## M6 result (2026-11-16)

| Part | Result | Evidence |
|---|---|---|
| End-to-end script | ✅ `dotnet run --project tests/Orchestrator.E2E`: a test repository, a published supervisor, a real session with two decisions | 8 of 8 checks pass |
| Crash while the session waits | ✅ The user answered while the supervisor was down. After the start, the answer arrived once | "the answer GREEN reached the session once" |
| Crash in the middle of a turn | ✅ The new supervisor waited for the old `claude` process, found the `session.input` of the turn, and marked it sent. No prompt went twice | `recovered turn …: the prompt arrived` |
| Store version | ✅ `version.json`. A newer store stops the supervisor | `RecoveryTests` |
| Unit tests | ✅ 62 tests | `dotnet test --solution Orchestrator.slnx` |

## B1 result (2026-11-16): new session form and mic buttons

The Sessions page has a form for a new session. Each free-text field of the cards has a mic button for dictation.

| Part | Result | Evidence |
|---|---|---|
| Form fields | ✅ Each brief field except `allowDefaults` | `NewSessionFormTests` reads `templates/brief/schema.json` |
| Form start | ✅ The form and `POST /api/sessions` use `Board.NewSession` | `NewSessionFormTests`: meta and `brief` event |
| Parent | ✅ A select of the sessions. An empty folder takes the folder of the parent | `NewSessionFormTests` |
| Refused start | ✅ The form keeps the text | `NewSessionFormTests` |
| Mic buttons | ✅ One for each `textarea` of the cards and the form | `NewSessionFormTests`, `WebTests` |
| No speech support | ✅ 0 of 3 mic buttons show | CDP check, Chrome without `webkitSpeechRecognition` |
| Phone layout | ✅ 390 px wide, no horizontal scroll | `docs/screens/sessions-new-form-phone.png` |
| All tests | ✅ 79 tests | `dotnet test --solution Orchestrator.slnx` |

Findings:

- A `Func` parameter does not render the parent component again. The Sessions page calls `StateHasChanged` after a start, or the toast does not show.
- `dotnet run --project src/Orchestrator.Web` serves `app.<hash>.css` with 0 bytes, because `ContentRootPath` is the `bin` folder. For a screenshot, use a copy from `dotnet publish`.
- Headless Chrome has a minimum window width of about 500 px, so `--window-size=390,…` cuts the page. Use the DevTools protocol: `Emulation.setDeviceMetricsOverride` with `width: 390` and `mobile: true`.
- The tests use bUnit 2.15.2 to render the Blazor components without a browser.
## D2 result: stop and archive (2026-11-16)

| Part | Result | Evidence |
|---|---|---|
| Cause of the M3 finding | ✅ `Process.Kill(entireProcessTree: true)` finds a descendant only through a parent that is alive. If a middle process ends first, its child is an orphan, and the kill misses it | `ProcessTreeTests.The_kill_of_dotnet_misses_a_descendant_whose_parent_ended` |
| Stop ends all descendants | ✅ `ProcessTree` scans the process list each 1 s while a turn runs and records each descendant with its start time. A stop kills each recorded process that is alive, also an orphan. A start time check prevents a kill of a reused process id | `ProcessTreeTests.Kill_all_ends_a_descendant_whose_parent_ended_before_the_kill` |
| Archive with a retry | ✅ `orch archive` tries the removal 5 times in about 7.5 s. If `git worktree remove` ran in this archive and removed the `.git` file but not the folder, the archive deletes the folder | `ProcessTreeTests.Archive_tries_again_while_a_file_in_the_worktree_is_held_for_a_short_time` |
| Stop, then archive, unit test | ✅ A process with an orphan held the worktree as its working folder. After the kill, the archive removed the folder | `ProcessTreeTests.Archive_removes_the_worktree_after_kill_all_ends_a_process_that_held_it` |
| Live test | ✅ A real session ran `powershell -Command "Start-Sleep 300; ..."` with the Bash tool. Chain: `dotnet` → `claude` → `bash` → `bash` → `bash` → `powershell`. `orch stop` ended the turn after 46 s. 5 s later, none of the 5 processes was alive. `orch archive` removed the worktree folder and kept the branch | Session `a834910f`, `ORCH_PORT=7812`, store in the scratchpad |
| Archive safety (review of PR #6) | ✅ The archive removes nothing if the folder is the repository root (full path compare) or if `<worktree>/.git` is not a file. A main checkout and a plain folder stay | `LifecycleTests.Archive_keeps_the_repository_folder_when_the_cwd_is_the_repository_root`, `LifecycleTests.Archive_keeps_a_plain_folder_with_no_git_file` |
| Unit tests | ✅ 78 tests | `dotnet test --solution Orchestrator.slnx` |

Findings:

- In the live test, all processes of the chain were alive at the stop. The old kill can possibly also end this chain. The orphan case of the unit test is the case that the old kill misses.
- A process that starts outside the tree (for example through a service or `Start-Process` of a COM server) is not a descendant. The stop does not end it. This is out of scope.
- A turn that ends normally does not kill its descendants. A background process of a tool can live on after the turn.

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
| Use the VSA framework for the orchestrator? | Not now. Plain .NET until 1.0; decide after the VSA 0.1.0 release |
| Tunnel: which tool and which sign-in? | Cloudflare Tunnel with Cloudflare Access |
| Store: files, or SQLite? | Files until M5 shows a real problem |
| `medium` has `checkDocs: true`. The user named the docs answer only for `low` | Keep it, and ask the user at the M4 start |
| Delete the old folder `Projects/claude-orchestrator`? It also holds a stray `session-orchestrator/` subfolder with copied type files | Yes, after the next app restart, when the user says so |
| Which vsa-template stream runs first in M5? | The user decided: no real stream yet (2026-11-16). First define what a stream is. Tests use the private copy `MgCohen/orch-m5-vsa-copy` with copied real issues. Delete the copy at the end |
| What is a stream, and what is its workflow? Does a session start sub-sessions by itself? Is the review flow automatic? Where is the flow defined? | Discuss with the user. Today the flow is in the prompts of `plugin/agents/orchestrator.md` and `plugin/agents/worker.md`, and in the `orch-*` skills |
| Does a Cloudflare account with a domain exist for the tunnel? | The user decided: Cloudflare, as the last step. Until then, local tests on this PC only |
| Which skills and agents does each kind of session get? The supervisor sessions may need other skills than the chat sessions | Discuss with the user. Today all sessions load all of `plugin/` (user review, 2026-11-16) |

### Known gaps

- PR comments show the user (`MgCohen`) as author. Each orchestrator PR title, body and comment starts with `[orch]`, so a reader can tell them apart.
- Supervisor sessions are not in the app sidebar. Open them with the app import (`docs/know-how.md`).

## Session management requirements (user, 2026-11-16)

| # | Requirement | Weight |
|---|---|---|
| R1 | The orchestrator session starts sessions when it needs them, with the settings and the context they need | Must |
| R2 | The sessions use the orchestrator tools | Must |
| R3 | The orchestrator session controls, adds and archives them | Must |
| R4 | They show in the desktop app, so the user can read their data and talk to them directly | Must |
| R5 | They show as attached child sessions in the app | Strong wish |
| R6 | One writer at a time (the lease). This is a limitation that we found, not a rule of the user. A shared session would be better. Review it later | Limitation |

The custom UI is out of scope for now. The two ways in are: the orchestrator session, or the user in the Claude Code app.

Findings of the probe tree:

- An app session starts an attached session only in a turn that a message of the user started. In a turn that an event or another session started, the app refuses the start. The session can propose a card, and the click of the user starts it.
- `orch enable` from the brief of a `start_session` session works: the tools, the inbox and the `live` lease come in the same turn, with no typed command.
- The store id (`CLAUDE_CODE_SESSION_ID`) of an app session is not its app id (`CLAUDE_CODE_HOST_SESSION_ID`). The store must record the app id.

## App session probes (2026-11-16)

Two app sessions, started from an app session with `start_session`, tested the plugin in the desktop app.

| Check | Result | Evidence |
|---|---|---|
| A child app session loads the plugin | ✅ The 4 `orch:` skills and the 3 `orch:` agents load. The old mod does not load. `orch` and `CLAUDE_CODE_PLUGIN_DIRS` work | Probe `plugin-probe` |
| `/orch-enable` from a message of another session | ❌ It arrives as plain text, not as a command. Only text that the user types runs a command | Probe `enable-probe` |
| `/orch-enable` typed by the user | ✅ `enabled`, lease `live`, heartbeat, and the 4 `mcp__orch__*` tools | Store session `a3f17453` |
| Inbox delivery in a live session | ✅ `orch send` PING reached the idle chat in about 2 s | `session.input` from `plugin` |
| `ask_user` and the answer | ✅ The decision showed in `orch decisions`. The free-text answer MANGO (not an option) arrived as "Answer to decision" | `decision`, `answer`, `delivered/` |
| `report` | ✅ `report.json` with state `done`, and a `report` event | `orch status a3f1` |

Findings:

- `/orch-enable` writes no `meta.json`. The session has no title (the queue shows its id) and no `cwd`. If its lease becomes `none`, the supervisor fails its turns with "no cwd".
- The reply of `/orch-enable` starts with `orch:` two times.
- An app session can be the root of a run only if the user types `/orch-enable` in it.
- Fixed the same day: `/orch-enable [title]` writes `meta.json` (title, cwd, `started: true`), the reply has no double prefix, and the hooks of a live session stop and release the lease when `orch archive` removes `enabled`. Not tested live yet.

## Sum test, run 1 (2026-11-16)

A root ("Sum test: root", `de4325e9`) got N = 4 from the user in the app and started 4 children with `spawn_child`. The supervisor started all sessions and imported each one into the app after its first turn. The policy level `test` (90 s) was added for this run.

| Child | Mode | Value | How the value came |
|---|---|---|---|
| 1 | auto | 47 | Random, no user action |
| 2 | ask | 7 | The user typed 7 in the child chat |
| 3 | chat | 13 | The user typed 8, 5 and "done" in the child chat |
| 4 | approve | 16 | Both steps took the default "yes" after 90 s |

The root summed 83 and reported done. Each child report reached the root inbox while the root was live in the app.

Findings:

| # | Finding | State |
|---|---|---|
| F1 | `ask_user` writes the question to the store only. In the app the user saw no question, and the child said "answer elsewhere" | Fixed: the tool tells the child to write the question in its reply text |
| F2 | An answer in the child chat did not close the decision in the store. `orch tree` showed "1 open" after the run | Fixed: a `composer` prompt in a session with an open decision answers the latest open decision with `orch answer` |
| F3 | The root did not see the questions of its children | Fixed: the supervisor sends each open question of a child to the parent inbox once (notice `child-question`, event `question.forward`). The root writes it in its reply. The new tools `mcp__orch__answer` and `mcp__orch__questions` let the root answer and list questions |
| F4 | An imported session has no Remote Control. The app setting "Connect new sessions to Remote Control" applies only to sessions that the user starts in the app | Open. `claude --desktop --resume <id> --remote-control` does not import: the process exits and the app gets no record. Only the app tool `set_remote_control` works, from an app session |
| F5 | Child 4 sent an extra "Step 1 is done" report before its final report | Open: the brief or the `orch-report` skill must say that a step is an update, not a report |
| F6 | The brief and the skill texts make the start of each transcript long in the app | Open: put the steps in a skill and keep the brief short |
| F7 | The user told the root "we are done with those four", and the root did not archive. The test brief said "Do not archive children" | Brief error. A test brief must not forbid what the user can ask for |
| F8 | `orch archive` archives the store side only. The app keeps the session in the sidebar. Only the app tool `archive_session` removes it | Open. Not known yet if an imported root has the app tools |
| F9 | The app refuses `archive_session` while Remote Control is on ("live work"), and while the session is open on screen | Turn Remote Control off first. The user closes or archives an open session |

## Sum test, run 2 (2026-11-16)

A new root ("Sum test 2: root", `ca72efff`) ran with the question relay. The 4 children gave 37, 5, 8 and 16. The total is 66. The user said that the flow works.

| Check | Result |
|---|---|
| The questions of children 2 and 4 reached the root inbox | ✅ notice `child-question`, event `question.forward` |
| The root showed each question in its reply | ✅ |
| The user answered child 2 in the root chat | ✅ the root called `mcp__orch__answer`. Child 2 got "use 5" and reported 5 |
| Child 4 took the default after 90 s | ✅ |

Findings:

| # | Finding | State |
|---|---|---|
| F10 | The policy answered step 1 of child 4. Then the root sent a user answer to the same decision. `orch answer` replaced the default answer, so the store lost the record of the default | Fixed: `orch answer` refuses a decision that has an answer (commit `b967593`) |
| F11 | The root does not know when a question closes (a default, or an answer in the child chat). Its list of open questions in its replies becomes old | Fixed: the supervisor sends a "Question closed" notice (event `question.close`). A question with no default that will apply gets a "Reminder" notice after 10 min, then each hour (event `question.remind`). Not tested live |
| F13 | The app asked the user to approve each `set_remote_control` and `archive_session` call. The app adds a field to these calls, so auto mode gave no verdict | Fixed: allow rules for the two tools in `~/.claude/settings.json`. In auto mode, both run with no prompt. The app still shows its own card to archive a session that is open on screen |
| F15 | `set_remote_control` with `enabled: false` shows a prompt: "A hook requires confirmation for this tool". It is a hook of the app, and the allow rule does not stop it. `enabled: true` shows no prompt. The app refuses to archive a session while Remote Control is on | Rule: turn Remote Control on for the root only. Turn it on for a child only when the user asks. Test with probes 2, 3 and 4: in auto mode with the allow rules, archive gave no prompt. Only Remote Control off asked, and only the session on screen was refused |
| F14 | An import (`claude --desktop --resume`) opens the session in the main window of the app and takes the focus. No CLI flag stops it. `open_session_in` acts only while the calling session is on screen | Workaround tested with probe 5: the focused session itself calls `archive_session` with `self`, and the app archives it while it is on screen. A message through the inbox (`orch send <id> ARCHIVE`) started the call. Built into `orch archive` (notice `archive-self`): probe 6, open on screen, was archived in the app and the store in 12 s |
| F12 | The format of the root replies is not fixed. It is not clear which items wait for the user and which are done. There is no rule for repeated notices of open questions | Fixed: the skill `orch:orch-reply` has a template for each reply (status block, short status, question). The agent, the notices and `ask_user` point to it. To change a format, change the skill. Not tested live |

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
