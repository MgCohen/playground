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
├─ plugin/         Claude Code plugin; loads in each session; inert unless the session is enabled
│   inbox → $.prompt.submit, mirror rows, heartbeat, status, lease, tools, skills, agents
├─ mod/orch-spike/ the spike mod of P0 (frozen)
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

## Watch notes (root "Orchestrator: root", `ab9acae5`, started 2026-11-16)

The session "Orchestrator watch" (`dc99fc00`) gets the framework problems of the root through its inbox. It records each one here and changes no code until the user decides.

| # | Problem | Analysis | State |
|---|---|---|---|
| W1 | `orch enable` in the old build session (`dc99fc00`) wrote `enabled`, but the plugin did not activate: no lease, no heartbeat | The session started before the current plugin hooks. Hot reload did not run `activateIfEnabled` in it. With lease `none`, a message in its inbox would make the supervisor run `claude -p --resume` on a live app session: two writers | Mitigated: `orch stop dc99fc00`, so the supervisor never runs it. A watcher reads its inbox |

## A2 result: communication code (2026-11-17)

The code follows the communication design, version 6 (`docs/communication-design.html`, tab "Changes A2"). All communication goes through the Stream API. `docs/store.md` has the new contract (store version 2).

| Part | Result |
|---|---|
| Stream API | `EventStream` (C#) and `plugin/hooks/stream.ts`: post, forward, ack, subscribe, query, read, nudge |
| Push | `StreamWatcher` (file watcher on `events/` and `nudges/`) with a 60 s sweep. The plugin runs a child `orch watch`; the supervisor and the UI use `StreamHub` |
| Ack and watchdog | Each delivery gets an ack. A live idle session with no ack gets a nudge after 15 s, then `delivery.blocked` after 60 s more |
| Types | New types `message`, `query`, `plan`, `ack`; `meta.json` in each type folder; new tools `send`, `query`, `plan`; skill `orch:orch-messages` |
| Removed | `inbox/`, `answers/`, `delivered/`, `reviews/`, `decisions/`, `report.json`, `reports/`, `update.json`, `cursor.txt`, `forwarded*`, the relay kinds and `unblocked` |
| Unit tests | 129 pass (`dotnet test --solution Orchestrator.slnx`) |

### Live sum test (scratchpad store, port 7791)

A root started 3 children with `spawn_child`. Child 1 picked 27. Child 2 asked the user; the test sent "my answer is: 9" to the root, and the root called `mcp__orch__answer` (`refs.via` = the root). Child 3 got the default 8 after 30 s. The root reported the total 44. Then `POST /api/inbox/<id>/accept` approved the default (a review to child 3) and acknowledged the root report.

| Check | Result |
|---|---|
| Each envelope to a session got an ack | ✅ 16 deliveries, 0 with no ack, no double ack |
| Question relay and closed notices | ✅ two forwards, two `question-closed` notices |
| Answer through the root | ✅ answer from `user` with `via: session:<root>` |
| Default for review in the inbox | ✅ card `answer [review]`, accept posted `review approve`, the child ran `orch decision ready` |
| Root report card | ✅ card `report [ack]`; the child reports closed when the root got their forwards |
| SSE | ✅ `GET /api/stream/live` sent the review envelope |

### Decisions on the open questions of the page

| Question | Decision |
|---|---|
| Sweep interval | 60 s, `delivery.sweepSeconds` in `policy.json` |
| Peer nudge from the supervisor | None. The supervisor nudges with the file `nudges/<party>`. A session sends a peer nudge after `send` to its parent |
| Render through `orch read --for` | One renderer in C#. `orch watch` puts the text in each line, so the plugin needs no extra call for the text |
| Chat line with two open decisions | No answer. `orch chat-answer` answers only when exactly one decision is open |
| Nudge of an app session whose process ended | Not built here: stream B owns the lease. The watchdog posts `delivery.blocked` |
| Wait times | 15 s and 60 s, `delivery` in `policy.json` |
| Old stores | No migration. The supervisor refuses a version 1 store |

### Findings

| # | Finding | State |
|---|---|---|
| A2-1 | `:` is not allowed in a Windows file name, so `subscriptions/session:<id>/` is `subscriptions/session-<id>/` | Decided, in `docs/store.md` |
| A2-2 | The envelope files had the computed fields `of`, `reply`, `via` and `isForward` | Fixed: `JsonIgnore`, and a test of the envelope fields |
| A2-3 | Envelopes of two writers in one millisecond can sort in any order | The ids of one writer always grow; a subscription keeps the done ids of the last 30 s |
| A2-4 | A child report goes to the user and is forwarded to the parent. The design did not say when its user card closes | It closes when the forward to the parent has an ack. A root report waits for the ack of the user |
| A2-5 | The live path (plugin deliverer with `orch watch`, stop latch, peer nudge) was not tested live: the test used supervisor turns only | Open: test it in an app session (A3 or A4) |
| A2-6 | `session.input` of a turn with several senders shows them joined by a comma | Open, cosmetic |

### Review of PR #11 (root, 2026-11-17)

| # | Item | Result |
|---|---|---|
| R1 | The delivery cursor passed an open envelope when a later envelope got an ack (for example in `ask_user` wait mode) | Fixed: the cursor moves only over acked envelopes before the first open one. Test `The_cursor_does_not_pass_an_open_envelope_when_a_later_one_has_an_ack` |
| R2 | "First answer wins" worked only in one process | Fixed: exclusive create of `claims/answer/<thread>` before the post. Tests with a claim of another process and with two stores in parallel |
| R3 | The TS post check accepted an unknown kind | Fixed: the plugin refuses an unknown kind and a message kind with no `meta.json`. The plugin posts no answer and no review, so the reply-type and first-answer checks stay in C#. Contract test through node |
| R4 | The watchdog ladder has no step 2 (peer message) | Known gap. The supervisor is not a Claude session, so it cannot send a peer message (decision on the design page: only sessions send peer nudges). The watchdog nudges with the file, then posts `delivery.blocked` after `peerSeconds`. A peer nudge from a live session that wakes an ended app process needs the lease rules of stream B |
| R5 | A late writer can lose an envelope: the id time is taken before two writes | The grace window is now 120 s, with a test for an envelope 60 s late. Limit: an envelope that appears more than 120 s after its id time can be missed. Recorded in `docs/store.md` |
| R6 | `docs/store.md` and the computed envelope fields | `docs/store.md` lists only the file fields, and says that the computed helpers are not in the file. A test checks the fields of an envelope file |
| R7 | Only `orch init` and the supervisor checked the store version | Fixed: each `orch` command except `doctor` and `template` checks it; the plugin stays inactive with a version that is not 2. Tests for the CLI and for the plugin constant |

Low items (recorded, accepted):

| Item | State |
|---|---|
| Redelivery after a crash of the hook between submit and ack (`deliver.ts`) | Accepted: delivery is at least once |
| Lost updates of `deliver.json` with no lock (two writers of the cursor) | Accepted: the ack is the truth, so a lost update only moves the cursor back and costs one more scan |
| The watchdog copied the full stream for each open envelope | Fixed: one set of blocked envelopes for each run |

Unit tests: 136 pass.

## A3 result: event stream code shape (2026-11-17)

Each reader asks the stream with one typed query, `StreamQuery`. No C# reader reads a session file for a message. `docs/store.md` has the contract (section Query).

| Part | Result |
|---|---|
| Typed query | `StreamQuery` (Core): since, until, after, kind, session, party, from, to, thread, needs, open, ledger, forwards, heads, newest, limit |
| Named queries | `StreamQueries.Inbox`, `OpenDecisions(session?)`, `Timeline(filter, limit)`, `Thread(thread)`, `Ledger` |
| Text form | `StreamQueryText.Parse`: one set of keys for the HTTP query string and for `orch events`. `view=<name>` starts from a named query |
| Routes | `GET /api/stream`, `GET /api/stream/inbox`, `/decisions`, `/timeline`, `/ledger`, `GET /api/stream/threads/<thread>`. `GET /api/stream/live` takes all keys |
| Kept routes | `/api/inbox`, `/api/events`, `/api/events/<id>`, `/api/stream/live`, `/api/decisions`, `/api/ledger` do not change their result |
| Readers moved | Inbox cards, open decisions (UI, API, `orch decisions`), timeline, tree, `orch status`, `GET /api/sessions` |
| Blocked | The newest `blocked` envelope about the session. `SaveBlocked` posts it when the reason changes and writes no `blocked.json` |
| Indexes | By kind and by sender. The watchdog, the question follow, `Report`, `Update` and `Decisions` do not copy the full stream |
| Cursor lock | `Done` reads, merges and writes the cursor under `<name>.json.lock` |
| Unit tests | 145 pass (`dotnet test --solution Orchestrator.slnx`) |

### Live sum test (scratchpad store, port 7793)

A root started 3 children with `spawn_child`. Child 1 picked 27. Child 2 asked (`high`, no default); the test sent "My answer for Sum child 2 is: 9" to the root, and the root called `mcp__orch__answer`. Child 3 got the default 8 after 30 s (`config/policy.test.json`). The root reported "Total: 44 (27 + 9 + 8)". Then `POST /api/inbox/<id>/accept` approved the default and acknowledged the root report.

| Check | Result |
|---|---|
| Each envelope to a session got an ack | ✅ 16 deliveries, 0 with no ack, no double ack |
| `GET /api/decisions` and `GET /api/stream/inbox` | ✅ the two open decisions, then the two cards `answer [review]` and `report [ack]` |
| `GET /api/stream?kind=report&from=session:<root>` | ✅ the root report |
| Answer through the root | ✅ answer from `user` with `via: session:<root>` |
| Inbox after the accepts | ✅ empty; a `review approve` went to child 3 |
| `GET /api/stream/live?kind=answer,report&needs=any` | ✅ the report envelopes |
| `orch tree`, `orch events --view ledger`, `--view timeline --limit 3`, `orch decisions` | ✅ |
| Cursor lock files | ✅ `<name>.json.lock` next to each subscription; no `blocked`, `delivery.blocked` or `turn.fail` |

### Findings

| # | Finding | State |
|---|---|---|
| A3-1 | The plugin still writes `blocked.json` in `deliver.ts`. No reader uses it | Done in Q1: the two writes are removed |
| A3-2 | `status.json`, `lease.json`, `heartbeat.txt`, `control.json` and `stack/` are lifecycle or git state, not messages | Kept as files. Stream B owns them |
| A3-3 | The global `orch` tool on this computer is older than A2 (no `orch init`). A live test must put its own `orch` first in `PATH` | Recorded. The live test used `dotnet tool install --tool-path` in the scratchpad. Install the global tool again after the merge |
| A3-5 | Request of A4: a card must show when its default applies | Done: `defaultAt` on each card of `GET /api/inbox` and on `Card.DefaultAt`. `DefaultRule.At` is the one rule; `IsDue` uses it. A docs check can still answer before that time |
| A3-6 | `dotnet test` stopped two times after 22 and 40 tests with "error: 1" and no failed test. The next 3 runs passed (145). A supervisor of the user listens on port 7777 during the runs | Open: watch it. Not reproduced |
| A3-4 | The inbox order changed a little: the cards are in the id order of the newest envelope of each thread | Accepted |
| A3-7 | Decision of the user: one message shape for a question. The kinds `decision` and `query` become one kind `question`. The target (`to`) gives the direction. A decision is a possible answer to a question | Done in Q1 (below) |

## B2 result: lifecycle code (2026-11-17)

The code follows the lifecycle design (`docs/lifecycle-design.html`, tab "B2 changes"). All lifecycle messages use the Stream API of A2: `session.state` and `state.conflict` events, notices to a session, and a `message` to the user for a root. `docs/store.md` has the contract.

| Part | Result |
|---|---|
| State machine | `Core/Lifecycle`: nine states, a detect check for each state, four global and 19 local guarded transitions with priorities and kinds, requests, the loop rule. No IO |
| State file | `state.json` and a `session.state` event for each change, request, cancel and escalation |
| Requested transitions | Stop, archive and auto-archive wait for their signals. A deadline escalates and does not change the state |
| Fallback | `orch:state-checker` (`claude -p`, read only) for a `state.json` that is not valid or a conflict. The result has `inferred: true` and starts no action |
| Lease process | The plugin writes `pid` and `started` from `orch process`. The supervisor releases a stale lease only when that process is gone, and runs no turn while the holder is `live` |
| Stop in the plugin | No delivery while `control.json` is not `active` |
| Not responding | Failed turns, stop latch, input not taken, hung turn, hung process; notices on enter and leave |
| Forgotten | One nudge (`report-missing`), then one `child-forgotten` notice (`forgotten.json`) |
| F5 | The report goes to the parent at the end of the turn, the last one only. The report tool refuses done with an open decision |
| F8 | Sidebar archive watch (`child-archived-in-app`); parent path at the archive deadline (`archive-child` with the app id); then the user |
| F4 | `background` in the brief and `meta.json`; `remote-control-self` notice after the import; the plugin records `remoteControl`; `remote-control-child` to the parent after `remoteControlSeconds` |
| Policy | `lifecycle` timers and `autoArchive` (off) in `policy.json` |
| API | `state`, `waitReason`, `requested`, `fault`, `inferred` in `GET /api/sessions` |
| Unit tests | 174 pass (`dotnet test --solution Orchestrator.slnx`). `LifecycleMachineTests` has the guard overlap test on 20 000 sample snapshots |

### Live test (scratchpad store, port 7823, plugin of the worktree)

A root and five children. Test policy: forgotten 1 min, stale process 30 s, reminder 1 min, auto-archive on after 2 min. `ORCH_IMPORT=0`, so no session went into the app.

| State | Path and action | Evidence |
|---|---|---|
| start, running, idle | `none → start (detect)`, `start → running (first-turn)`, `running → idle (turn-end)` | Each session |
| forgotten | `idle → forgotten` after 1 min; nudge `report-missing`; `forgotten → running (nudge-turn)` | Forgotten child `84d264f2` |
| done | `running → done (report-done)`. Two done reports in one turn: the first got an ack `superseded`, the second went to the root | Forgotten child |
| done, archived | `archive-reminder` to the root after 1 min; `done → archived (auto-archive)` after 2 min; `child-archived` | Forgotten child |
| stopped | `orch stop` in a running turn: `running (requested: stopped)`, then `running → stopped` 0.3 s later; `child-stopped`. `orch resume`: `stopped → idle (resume)`, `child-resumed`, the inbox went at once | Stop child `dc3a422c` |
| archived | `orch archive --store-only` in a running turn: `running → archived (archive)`, `child-archived`, the turn stopped | Stop child |
| stale, not responding | Live lease with the id of a running `powershell`: `idle → stale`, `stale → not-responding (process-hangs)` after 30 s, `child-not-responding`. The state stayed. After the kill: `not-responding → stale`, `stale → idle (process-gone)`, `lease.release` | Stale probe `11111111-b200-4000-8000-000000000003` |
| unknown | A `state.json` with state `sleeping`: the real `orch:state-checker` inferred `idle` in 14 s; `state-inferred` to the root | Fallback probe `11111111-b200-4000-8000-000000000004` |
| F5 refusal | A child called `ask_user`, then `report` done: the tool returned the refusal text, and no report was posted | Early done child `856cb929` |
| Lease process | During a turn, `lease.json` had `pid` 185876, the process `claude.exe` | Root `c325cfbd` |

### App probes

| Probe | Result |
|---|---|
| Can a session call `set_remote_control` on itself, and can the root call it for a child? | Run by the root (decision of the user): see "App probes (root, 2026-11-17)" below |
| Can the root call `archive_session` for a child by its app id? | Yes, after Remote Control off: see below |

The F4 and F8 code works with both probe results: the self path is the first step, and the parent path is only an escalation after a deadline. If a parent call fails, the parent tells the user.

### Findings

| # | Finding | State |
|---|---|---|
| B2-1 | From `not-responding` (hung process), the global `stale` fired again, so the state flipped at each tick, and each flip sent a notice | Fixed in the live test: `stale` excludes a hung process. Tests `A_hung_process_stays_not_responding…`, `A_loop_from_not_responding…` |
| B2-2 | The loop rule then gave `not-responding → not-responding` at each tick | Fixed: a loop in `not-responding` changes nothing |
| B2-3 | An archive committed while the supervisor turn still ran | Fixed: an archive also waits for "no supervisor turn" |
| B2-4 | The design counts each transition for the loop rule. A normal session takes `idle → running` many times in one hour | The main path does not count (`decisions.md`) |
| B2-5 | A supervisor turn can start and end between two ticks, so the machine sees no `running` | Extra local transitions: `idle → done`, `done → idle`, `forgotten → done`, `forgotten → idle`, `not-responding → idle` (`decisions.md`) |
| B2-6 | A state that the detect checks find after a supervisor restart sends no notice (no "from" state) | Open. A restart does not repeat old notices, but a new fault found at a restart is not told to the parent |
| B2-7 | Each fallback is a finding: the supervisor log has a `FINDING:` line | One in the live test, made by hand. No conflict happened |
| B2-8 | The new session form has no `background` field | Open for A4 (UI). A form session has Remote Control on, the default |

### Process tree: the test abort of the root review (2026-11-17)

The root review saw `dotnet test --solution Orchestrator.slnx` end with no error and no failed test (after test 3, 39, 65 or 107 of 144, on the A2 and A3 branches). The suspect was a kill of a wrong process by `ProcessTree`.

| Check | Result |
|---|---|
| Reproduction on B2, old tree code | Not reproduced: 6 runs of the test exe and 5 runs of `dotnet test --solution`, each 174 of 174 |
| Can the old tree record the test process or its parent? | No. A recorded process must start after a recorded parent, and the test process and its parent are older than each root of a tree. Test `A_real_tree_never_records_the_test_process_or_its_parent` |
| Can the old tree record a wrong process? | Yes, by two races of pid reuse (below). Each one can end an unrelated new process at a stop, also in production |
| Can the test teardown end a wrong process? | Yes. `EndLeftovers` ended the pid from the pid file with no identity check, after `KillAll` had ended that process. A new process with that pid was ended |

The two races of the old `Scan`:

1. The snapshot lists a child C of a recorded process. C ends, and a new process takes the id of C before `Scan` reads the start time. The new process was recorded, because its start time is later than the parent.
2. A recorded process K ends. A short-lived process takes the id of K, starts a child and ends. The child names K as parent and started after K, so it was recorded.

Fix:

| Part | Change |
|---|---|
| `Processes.Open` | Opens a handle and reads the parent id and the start time from that handle, not from the snapshot |
| `ProcessTree` | Keeps the handle of each recorded process until `Dispose`. Windows does not give the id of a process to another process while a handle to it is open, so race 2 cannot happen. A new child is recorded only when its own handle names the parent and its start is not before the parent (race 1). A kill uses the held handle, so it cannot end another process |
| `IProcessTable` | The tree reads a process table. The tests give a fake table that gives an ended id again, as Windows does, but never while a handle is open |
| `ProcessTreeTests.EndLeftovers` | Ends the orphan through a handle that the test opened while the orphan was alive |

| Test | Result |
|---|---|
| `ProcessTreeRaceTests` (6 tests: stale snapshot, older process with the parent id, orphan and held handle, quick process with a reused id, real tree and the test process, handle values) | Pass |
| The 4 old `ProcessTreeTests` | Pass |
| Full suite after the fix, 4 runs of `dotnet test --solution` | 180 of 180 in each run |

Limit: a process that ends before the first scan that sees it is not recorded, and its children are not found. This is the same as before (scan each 1 s). A Windows job object would find each descendant with no scan; it is not built.

Probable cause of the abort: the old kill code of the tree, now fixed. The Windows Application log has no crash event of `Orchestrator.Tests.exe`, so the test process ended by a kill, not by an unhandled exception. The races above could end a wrong process, and the test teardown ended a pid with no identity check. After the handle fix, the root saw 2 full runs with no abort (184 of 184). An earlier guess, a timeout kill of the Bash tool after 120 s, does not fit: the root runs used a timeout of 600 s.

Open: the cause is not proved by a reproduction. If the abort comes again, run the test exe with `--output Detailed` to see the last test, and look in the Windows event log (Application) for a crash of `Orchestrator.Tests.exe`.

### Child sessions in the app from their start (user decision, 2026-11-17)

The user cannot follow a child before its first turn ends, because the supervisor imported a session only after its first good turn. The user decided: show each child in the app by default, from its start, with Remote Control on (off only for a background brief).

| Part | Change |
|---|---|
| Opening turn | With `ORCH_IMPORT` on, the first turn of a new session has only the notice `open-in-app`: reply READY, no work, no tool. The app can import only a session that has a transcript, so this short turn is the earliest point |
| Import | Right after the opening turn. The brief and the other envelopes stay undelivered |
| Brief | The plugin of the live app session delivers it, so the work runs in the app. If the app does not take the lease in `ImportWait` (30 s), the supervisor delivers it with `claude -p --resume` |
| Remote Control | `remote-control-self` after the import, as before. A background session is in the app too, with Remote Control off |
| Tests | `ImportTests`: the opening turn, the import before the brief, the fallback delivery, no opening turn with no import. 182 of 182 pass |

Not tested live: the import opens the session in the main window of the app and takes the focus (F14), so a live test disturbs the user. The root can run it with the app probes.

### App probes (root, 2026-11-17)

Probe session "Probe: archive target" (`6850a633`): started with `spawn_child`, imported after its first turn, Remote Control off after the import.

| # | Probe | Result |
|---|---|---|
| 1a | A child, live in the app, calls `set_remote_control` on itself | ✅ `{"remoteControlState":"on"}`, no prompt |
| 1b | The root calls `set_remote_control` for a child app id | ✅ 6 times today (A1, B1, A2, A3, A4, probe), each "on", no prompt |
| 2a | The root calls `archive_session` for a child with Remote Control on | ❌ Refused: "it still has live work (an agent run, a Remote Control client, a queued message or a background task)". Confirms F9 |
| 2b | The root calls `set_remote_control` with `enabled: false` for the child | ✅ "off", at once (auto mode, allow rule) |
| 2c | The root calls `archive_session` for the child with Remote Control off | ✅ "Archived session local_6850a633-…" |
| 2d | `orch archive 6850a633` | ✅ Archived the store side |

Changes from the results:

| Part | Change | Probe |
|---|---|---|
| `archive-self` | The session turns its Remote Control off, then calls `archive_session` on `self`. Before, it called only `archive_session`, which fails now that Remote Control is on by default | 2a |
| `archive-child` | The parent turns the Remote Control of the child off, then calls `archive_session` with the app id | 2a to 2c |
| Parent path of F8 | A session that the app shows but that is not live gets `archive-child` to its parent at once, not at the deadline of 120 s: it cannot archive itself. Test `A_child_that_the_app_shows_but_that_is_not_live_gets_the_parent_archive_path_at_once` | 2c |
| `remote-control-self` | Says to ignore the notice when the session has no app tools (a `claude -p` supervisor turn): the parent path follows after `remoteControlSeconds` | 1a, 1b |
| Plugin record | A `set_remote_control` call with a child app id (`local_<id>`) writes `remoteControl` in the `meta.json` of the child | 1b |

Open: the self call `set_remote_control` with `enabled: false` on `self` showed the prompt "A hook requires confirmation for this tool" in F15. Probe 2b ran from the root on a child and had no prompt. The `archive-self` path can still show the prompt in the child. The parent path does not.

### Review of PR #14 (root, 2026-11-17)

| # | Item | Fix | Test |
|---|---|---|---|
| 1 | High: the brief went two times, with two writers, when the user opened the app session while a `claude -p` turn ran with the brief | The plugin does not take the lease and does not deliver while `turn.json` has no end and the lease is `supervisor` (or the turn is less than 120 s old) | `PluginLeaseTests` (node, fake engine): the scenario of the review, then the normal path after the end |
| 2 | High: the parent plugin wrote the `meta.json` of a child, and the child plugin wrote its own `appId` | The plugin posts `meta.update`; only the supervisor writes `meta.json` (`appId` of the sender, `remoteControl` of the sender or its child) | `Only_the_supervisor_writes_meta_and_it_applies_the_allowed_meta_updates`, `The_plugin_writes_no_meta_of_another_session…` |
| 3 | Medium: forgotten went back to idle before the nudge turn | `remembered` waits while an input has no ack | `A_forgotten_session_waits_for_the_turn_of_its_nudge…`, `The_nudge_turn_goes_forgotten_to_running…` |
| 4 | Medium: `forgotten.json` was never cleared | A new report or a new input from another party starts a new episode; the file goes when the session leaves forgotten for done, stopped or archived | `…a_new_input_starts_a_new_forgotten_episode` |
| 5 | Medium: a user ack hid a report from the parent | The relay uses one subscription for each session and ignores the user ack | `A_user_ack_of_a_report_before_the_turn_ends_does_not_hide_it_from_the_parent` |
| 6 | Medium: the tree kept each handle until `Dispose`; `KillAll` and `Dispose` could run at the same time | The handle of a process that ended before a scan snapshot goes after that scan; one lock for the kill and `Dispose` | `An_orphan_is_killed…`, `A_parent_that_ends_after_the_snapshot…`, `Kill_all_and_dispose_at_the_same_time…` |
| 7 | Low: a stopped opening turn kept `Started`, so the import waited for the brief turn | The `open-in-app` notice with no ack gives the opening turn again, as a resume | `A_stopped_opening_turn_is_followed_by_the_opening_turn_again…` |
| 8 | Low: F5 forwarded a report in the middle of a live turn with an old heartbeat | The turn stays open while `status.json` says running and the lease is `live` | `A_report_of_a_live_turn_waits_while_the_heartbeat_is_old…` |
| 9 | Low: the lifecycle code read all events for each session at each tick | Reports by subscription, the Remote Control check by thread, the snapshot by `Since` windows (24 h maximum). A turn with no own event yet is not hung | `The_remote_control_notice_has_its_own_thread…`, `A_supervisor_turn_with_no_event_yet_is_not_hung…` |
| 10 | Docs: the design page said "first turn with the brief, then import" | The state `start` describes the opening turn. Page published again | |
| 11 | Docs: the guess of a 120 s tool timeout | Changed: probable cause is the old kill code, now fixed | |

Regression found in the fix of item 9, and fixed: the snapshot used the hung time as the default when the hung window had no event of the session. A supervisor turn that counted as running before its `turn.start` was posted looked hung (`not-responding`), in about 2 of 7 runs of one test. Now an empty hung window falls back to the 24 h window, and no event at all gives no hung fault. Test `A_supervisor_turn_with_no_event_yet_is_not_hung_and_a_turn_with_old_events_is`.

Finding B2-9 (open, A2): `FileStore.Report` and `FileStore.OpenDecisions` still copy the full stream (`Events.All()`) at each call, and the snapshot calls them for each session at each tick. An index by session in `EventStream` would fix it. This is the transport of A2, so B2 does not change it.
Unit tests after the review fixes: 197 of 197 pass (`dotnet test --solution Orchestrator.slnx`).

## Q1 result: one question kind (2026-11-17)

Branch `orch/session-913e5c4c` on top of `orch/integration-a3-b2` (PR #17: A3 + B2, 206 tests pass).

| Part | Result |
|---|---|
| Template | `templates/question` replaces `templates/decision` and `templates/query`. The PR renders moved to `question/render/` |
| Field rule | Only `question` is required. `dependentRequired: { defaultOption: [risk] }` in `schema.json`: the C# post check and the plugin check read it there |
| Kinds | `EventKinds.Question`; `EventKinds.Questions` = { question }. The stream reads the old kinds `decision` and `query` as `question` (`EventKinds.Current`), so the store version stays 2 |
| Policy form | `Decision.Kind` is `ask_user` for a question with a risk or a default, else `query`. Only `ask_user` gets a docs check and a default. No risk reads as `high` |
| Tools | `mcp__orch__ask_user` takes both forms. `mcp__orch__query` is removed. A refused post returns the error text as the tool result |
| A3-1 | The plugin writes no `blocked.json`. The blocked state is the newest `blocked` envelope; `Blocked.By` comes from its sender |
| Inbox fields for A4 | No field removed: `id, kind, needs, session, title, envelope, fields, defaultAt`. The value of `kind` is now `question` (was `decision` or `query`). `meta.json` quick fields add `context` |
| Unit tests | 205 of 205 pass. Two per-kind template cases fewer (one kind fewer), one new test for the field rule |

### Live sum test (scratchpad store, port 7795, plugin of the worktree)

A root started 3 children with `spawn_child`. Child 1 picked 27. Child 2 asked with only `question` and `context` (no risk, no default); the test sent "My answer for Sum child 2 is: 9" to the root, and the root called `mcp__orch__answer`. Child 3 first called `ask_user` with `defaultOption` and no risk, then again with risk `medium`; the policy applied the default 8 after 30 s. The root reported "Total: 44 (27 + 9 + 8)".

| Check | Result |
|---|---|
| Only the kind `question` in the stream | ✅ 4 `question` envelopes (2 posts, 2 forwards), no `decision` or `query` |
| A default with no risk is refused | ✅ tool result "orch: the question was not recorded: question: defaultOption needs the fields risk." No envelope was posted |
| A question with no default | ✅ no docs check and no default; open until the answer through the root (`via: session:<root>`) |
| A question with a default | ✅ `answer` from `policy` to the child and to the user; `defaultAt` on the inbox card |
| Relay and closed notices | ✅ two forwards to the root, two `question-closed` notices |
| Each envelope to a session got an ack | ✅ 14 of 15; the one with no ack was the `archive-reminder` notice of the last second |
| Inbox accept | ✅ a `review approve` to child 3, an `ack` for the root report; the inbox is empty after |
| `blocked.json` | ✅ no file in any session folder |


## T1: Desktop tray app (visual only)

- `src/Orchestrator.Tray`: a WinForms tray icon and a widget with a BlazorWebView, one instance at a time. The design is the same as the runner widget.
- Widget: 350 × 720 logical pixels scaled to the DPI (lower if the work area is smaller), no title bar, rounded corners, a 1-pixel border, not in the taskbar. It opens at the home location in the corner above the tray.
- Tray icon: a left click shows or hides the widget. The menu has Show, Pin on top (TopMost) and Exit. `--pin` starts the widget pinned. `--hidden` starts with the widget hidden.
- Drag: a drag of the page header moves the widget (the runner code: `ReleaseCapture` and `WM_NCLBUTTONDOWN` with `HTCAPTION`). The new place goes to `%LOCALAPPDATA%\Orchestrator.Tray\place.json`. The next show and the next start use it, if the full widget is on a screen. Else the widget uses its home location.
- Pin: a pin button in the page header sets or removes TopMost. The tray menu item and the header button show the same state (`IWidgetWindow.PinnedChanged`).
- A4 change for the header icon only: `PageHeader.razor` replaces the header of Home, Cards, Stream, Sessions and Settings. It also adds the `pin` icon to `Ui.Icons`. On the web server there is no `IWidgetWindow`, so the header has the same markup as before. The pin style is in the tray `tray.css`, not in `app.css`.
- The widget shows the A4 views of `Orchestrator.Web` (PR #15, merged into this branch) through a project reference. No change in `Orchestrator.Web`.
- Mock data: a new store in a temporary folder (`MockStore`). The real store does not change. No tick service, no claude process (`NoRunner`).
- Self-test (`.\tray.ps1 -SelfTest`): PASS for 8 views and the widget check: size 525 × 840 pixels at 144 DPI, at the home location. It also copies the screen corner to `tray-desktop-corner.png`.
- Self-test of the behaviors: PASS. Pin: two clicks set TopMost off, then on. Drag: a real mouse drag (SendInput) moves the widget from 2017,670 to 1777,490, and the place file has 1777,490. Toggle: hide, then show again at the saved place. The drag check moves the mouse pointer for about one second.
- `dotnet test --solution Orchestrator.slnx`: 159 passed, 0 failed.
- Screenshots: `docs/screens/tray-*.png` and `docs/screens/tray-desktop-corner.png`.
- Finding: in the corner screenshot, the tray icon is in the hidden icons area (^) of Windows. To show it next to the clock, drag it out of that area once.
- Finding (A4): at the old widget height of 560, the Cards page puts the action buttons behind the bottom app bar. The self-test opens the answer sheet with a script click on the left button.
- Finding (A4): on Session status, the sticky Message and Event stream bar covers Stop and Archive until the user scrolls.
- `install.ps1` is not run yet. It needs the approval of the user.
## I1 result: T1 + Q1 integration, live tray (2026-11-17)

Branch `orch/integration-t1-q1`: Q1 (`orch/session-913e5c4c`) with T1 (`orch/session-88155eec`, the tray widget and the A4 views) merged in.

| Part | Result |
|---|---|
| Merge conflicts | `docs/plan.md`: both sides kept. `EventCard.razor` and `Timeline.razor`: deleted, as in T1 (the A4 views replace them) |
| A4 views on the kind `question` | `Ui.cs`, `Open.razor`, `EventRow.razor`, `MockStore.cs`, `DemoSeed.cs` and `InboxUiTests.cs` use `EventKinds.Question`. `DecisionKinds.Of(fields)` gives the sub-type: `ask_user` with a risk or a default, else `query`. The label is "Decision" or "Question", and the field rows and the card actions follow the sub-type. The look does not change |
| Scroll bar | `tray.css` hides the browser scroll bar in the widget. The page scrolls with the wheel, touch and keys |
| Height | Request of the user: a taller widget with the same width. 350 × 720 logical pixels (525 × 1080 at 144 DPI). On a smaller work area the height is the work area height less the two gaps |
| App bar backdrop | Request of the user: content under the bottom app bar mixed with it (the "WAITING · NEWEST FIRST" list). A fixed layer (`body::after`, 92 px, z-index 9) under the app bar fades the content into the canvas. It lets clicks through and stays below the sheets and the toast |
| End of each view | The app bar is sticky in the page flow, so its own space is the bottom padding of the page. At full scroll the last content ends at 619 px (home, cards, stream) of a 719 px view: above the backdrop (627 px) and the home button (634 px). On Cards the actions Other, skip and Accept show above the bar. Self-test check `scroll-end` |
| Taskbar gap | The home location has a gap of 12 logical pixels above the work area (18 px at 144 DPI), as the runner. A saved place or a drop on the edge moves in to the same gap (`WidgetForm.Fit`) |
| Live mode | `Orchestrator.Tray --live <store> [--port <port>]` (`tray.ps1 -Live <store> -Port <port>`). The tray writes with the writer id `tray` and runs no tick (`SupervisorOptions.Ticks` false). The supervisor process of the store delivers. Mock mode stays the default |
| Unit tests | `dotnet test --solution Orchestrator.slnx`: 221 passed, 1 skipped (`Seed_a_demo_store`, explicit) |
| Tray self-test | 17 of 17 PASS: 8 views and the checks widget (525 × 1080), taskbar-gap (18 px), no-scroll-bar (0 px), app-bar-backdrop, scroll-end, desktop-corner, pin, drag and toggle. Desktop corner screenshots at the new height (mock data): `docs/screens/tray-corner-home.png`, `tray-corner-cards.png`, `tray-corner-stream.png` |

### Live test (scratchpad store, port 7841, plugin of the worktree)

A supervisor of this branch (published copy, `ORCH_IMPORT=0`, the `orch` tool of this branch first in `PATH`) and the tray in live mode on the same store. The policy level medium had a timeout of 420 s, so the timer shows on the card. The root started 3 children with `spawn_child`.

| Check | Result |
|---|---|
| Real question cards in the widget | ✅ "Question · Sum child A" (no default, the context row "Why") and "Decision · Sum child B" (options 8 and 12, default 8) |
| Default timer (`defaultAt`) | ✅ The card of child B showed the ring with 6:19 left. The policy answered 8 at 05:47:02 |
| Answer from the widget | ✅ Type, "9", Send. Envelope `1794890478426-cb7773ae-tray`: `answer` from `user` to child A, reply to the question |
| The answer reached the child | ✅ Child A ran a turn at once and reported "Child A: 9". The session stream shows question, answer and report |
| Sum | ✅ The root reported "Total: 44 (9 + 8 + 27)" |
| Default for review | ✅ The card "Review · default" for child B: "8 · level medium, no answer in 420 s" |
| No tick in the tray | ✅ All turns, relays and the default came from the supervisor process |

The live screenshots show the widget at the earlier height of 560. Screenshots (desktop corner, live data): `docs/screens/tray-live-home.png`, `tray-live-card-a.png`, `tray-live-card-b.png`, `tray-live-answer-sheet.png`, `tray-live-answer-sent.png`, `tray-live-stream.png`, `tray-live-sessions.png`, `tray-live-session-status.png`, `tray-live-session-stream.png`, `tray-live-card-review.png`.

### Findings

| # | Finding | State |
|---|---|---|
| I1-1 | A Git Bash `PATH` entry with a drive letter (`C:/...`) splits at the colon. The first root used the old global `orch`, which needs `templates/brief/render.md`, and `spawn_child` failed. With `/c/...` the test `orch` was first | Test note: use the `/c/...` form. Install the global tool again after the merge (A3-3) |
| I1-2 | The tray in live mode does not know the turns of the supervisor process, so `IsRunning` is false in the tray. The views show the lease and the state files | Accepted |
| I1-3 | A script drove the widget through the WebView2 debug port (`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=<port>`) with clicks on the real buttons. No code change | Test note |
| I1-4 | The supervisor sent "Archive ... from the app sidebar" messages for the done children, with `ORCH_IMPORT=0` | Open: examine if these messages are necessary when no session was imported |

## A4 design: inbox UI (2026-11-17)

The user approved the design after four rounds. The design page is `docs/inbox-design.html` (https://claude.ai/artifact/RbSF83MgxWHbWo5V3JsGEE).

| Part | Decision |
|---|---|
| Theme | Porcelain Orange of Runner Studio: raised cards, carved wells, arc rings. Doto for titles and numbers, Inter for text, JetBrains Mono for ids and times. Light and dark |
| App bar | A floating pill: Cards, Stream, Home (raised orange circle in the center), Sessions, Settings. A dot under the active side item |
| Home | A deep ring of the waiting cards by type (bright colors, carved ring, raised center), active sessions that fold open with "See all", events of the last 24 h as dots, and the waiting items as a list that grows. An empty state for each block |
| Cards | One pile in arrival order. Right and left actions come from the card type. Down skips: the card goes to the end of the pile, and a new card goes after it. A tap opens the full document |
| Card types | Yes or no: Yes / No. Default: Accept / Other. Opinion and question: Speak (mic on) / Type. Review and plan: Approve / Change. Report and blocked: Got it / Reply |
| Undo | The UI waits 5 s before it sends an act. A toast shows Undo |
| Filter and order | One sheet for Cards and Stream: order, date, sessions, source |
| Open | The full render of the template. A closed item opens the same way; its actions are off and the result shows who closed it |
| Stream | All events, newest first, on a timeline with ring nodes. The session stream is the same view, locked to one session and its children |
| Sessions | One foldable tree. A status page for each session: state, numbers, children, related artifacts, details, and the event stream |
| Version 2 | A form for each message type, made from its schema |

Findings:

| # | Finding | State |
|---|---|---|
| A4-1 | The Design canvas type is slow for small changes. A plain HTML page with static frames was faster for the review rounds | Decided: plain page |
| A4-2 | The skip order stays in the UI session. On another device the pile goes back to arrival order | Accepted |
| A4-3 | A card shows the preview of its template, Open shows the full render. The templates have no preview render file; the UI uses the card fields of `meta.json` and fixed fields for each type | Open: a preview render for each template (templates are not in the A4 scope) |

## A4 result: inbox UI build (2026-11-17)

The Blazor UI follows the approved design. It is stacked on A3 (`orch/session-5b4701b7`) and uses `defaultAt` of the inbox cards for the timer ring.

| Part | Result | Evidence |
|---|---|---|
| Home | ✅ Deep ring of the waiting cards by type, active sessions that fold open, events of 24 h as dots, the waiting list, empty states | `InboxUiTests`, `docs/screens/inbox-home-phone.jpg` |
| Cards | ✅ One pile in arrival order. The card type sets the right and left actions. Skip puts the card at the end; a card that arrives later goes after it | `InboxUiTests`, `docs/screens/inbox-cards-phone.jpg` |
| Undo | ✅ An act waits 5 s; Undo cancels it. A view that closes sends the waiting act | `InboxUiTests` |
| Answer sheet | ✅ Suggestions, free text, mic. Speak starts the mic | `InboxUiTests`, `docs/screens/inbox-answer-sheet-phone.jpg` |
| Gestures | ✅ Pointer swipe right, left and down on the top card (small JavaScript, `swipe.js`). A tap opens the document | Browser pane, a pointer sequence: skip moved the card to the end |
| Stream | ✅ Timeline with ring nodes, views Messages / All events / Waits, filter and order sheet. `?session=` locks it to a session and its children | `InboxUiTests`, `docs/screens/inbox-stream-phone.jpg` |
| Sessions | ✅ Foldable tree, search, new session form | `NewSessionFormTests`, `docs/screens/inbox-sessions-phone.jpg` |
| Session status | ✅ Progress ring, counts, children, related artifacts (report links), details, stop, resume, archive, message, event stream | `InboxUiTests`, `docs/screens/inbox-session-status-phone.jpg` |
| Open | ✅ The full render of each envelope. A closed item shows its result, and its actions are off | `InboxUiTests`, `docs/screens/inbox-open-closed-phone.jpg` |
| Unit tests | ✅ 159 pass, 1 explicit seed test skipped | `dotnet test --solution Orchestrator.slnx` |

The live test used a scratchpad store and port 7795 with demo data from the explicit test `DemoSeed` (`ORCH_SEED=<folder> dotnet test --project tests/Orchestrator.Tests --filter "FullyQualifiedName~DemoSeed"`), and a fake `claude` (`ORCH_CLAUDE`) that prints one success result line. The browser pane emulated 390 × 844.

Findings:

| # | Finding | State |
|---|---|---|
| A4-4 | `.card` was the class of the pile card and of the new session form, so the form took the absolute position of the pile card | Fixed: the pile card is `.pcard` |
| A4-5 | A toast at the bottom covered the action buttons, so a tap hit the toast | Fixed: the toast is at the top |
| A4-6 | The Messages view showed each forward to the root as a second row with another state | Fixed: Messages hides forwards; All events shows them |
| A4-7 | The drag tool of the browser pane sends too few pointer moves for a swipe. A pointer event sequence works | Test note |
| A4-8 | The supervisor runs a turn in the `Cwd` of the session. A demo store needs a folder that exists | Test note: the seed uses the temp folder |
| A4-9 | A child report card closes when the root gets its forward (A2-4). With a working root, the user sees few report cards | Open: confirm with the user that a child report needs no card when the root got it |
| A4-10 | The pile order, the skip order and the filter stay in the UI circuit. A reload goes back to arrival order | Accepted (A4-2) |

## R1 result: document render and preview, first setup (2026-11-17)

Branch `orch/session-d7cf20ac` on top of Q1 (`orch/session-913e5c4c`). This is a first setup for review with the user, not a final design. Design page: `docs/render-design.html` (https://claude.ai/artifact/VvGbvARDGC1gxmfUqT99af).

| Part | Result |
|---|---|
| Render spec | `templates/<kind>/view.json` for each of the 10 kinds: `label`, `icon`, `tone`, optional `toneBy`, `document` (title, subtitle, facts, blocks) and `preview` (title, line, well, chips, list, pill, progress) |
| Render model | `Orchestrator.Core.Templates.Views`: `Document(set, kind, fields)` gives a `DocumentModel`, `Preview(set, kind, fields)` gives a `PreviewModel`. `TemplateSet` reads `view.json` into `Template.View` |
| Looks | A field spec has `as`: `text`, `code`, `well`, `list`, `steps`, `chips`, `links`, `pill` or `progress`. A pill maps each value to a color token (`tones`) |
| Titles | A title is a list of templates in the `render.md` syntax. The model uses the first one that has all its fields. `{{render:kind}}` is the first line of the render file that the field names (for a notice) |
| Unit tests | `ViewTests`: one test for each kind with its `example.json`, plus checks that each view names only schema fields and gives a title. 250 of 250 pass |
| Design page | `docs/render-design/page.html` is the source, with the inbox tokens of `docs/inbox-design.html` (branch `orch/session-06ade234`). `DesignPageTests` builds `docs/render-design.html` from it and each `view.json`, `example.json` and purpose, and fails if the page is not current. To write the page again: `ORCH_WRITE_DOCS=1 dotnet test --solution Orchestrator.slnx` |

### Render of each kind

| Kind | Document: title · facts · blocks | Preview: title · well or list · pill |
|---|---|---|
| question | question · Risk, Default · Context, Snippet, Options (default marked), Recommendation | question · Recommendation, else Context · options chips, Risk |
| answer | text, subtitle question · By, Then · Reason, Source | text · Reason, else Source · By |
| review | question, else "Review of <decision>" · Verdict, About · Direction, Branch, Base, Next branch | question · Direction · Verdict |
| report | summary · State · Blockers (failed tone), Done, Next, Links, Details | summary · Blockers, else Done, else Next · State |
| update | summary, subtitle step · Progress | summary · progress bar |
| brief | title · Agent, Folder, Worktree, Background, Allow defaults · Goal, Scope, Done when, Skills | title · Done when |
| notice | first line of `render/<kind>.md` · Kind, Session, Child · full render text | first line · Kind |
| plan | title · none · Context, Steps | title · first 4 steps |
| message | text · none · none | text |
| ack | "<state> · <by>" · State, By | "<state> · <by>" |

A report with state `blocked` uses the tone `failed` and the icon `alert` (`toneBy`). The stream row uses the preview title and `line`.

### Findings and notes

- The A4 views do not use the model yet. The integration child replaces `Open.razor` `Rows`, `CardPreview.razor` `Well()` and the kind part of `Ui.Look` with `Views`.
- `meta.json` `card.quick` and the preview spec overlap. Keep `card` for the actions (accept, refuse) only, and remove `quick` after the views use `Views`.
- The first sentence of a notice render is not a good title: the question text has a `?`. The model uses the first line.
- Review of PR #20 (root): no Python build script; the stack allows no new script language. The build step is the C# test `DesignPageTests`.
- Forms (later part): a form can use the same `view.json` field order and labels, with the input type from `schema.json`.

### Review decision (user, 2026-11-17)

Decision `toolu_01N98MJeeSqHZoy8YUVVZEN5`: the user approves the system. Each kind defines its full document and its preview in `view.json`, and the app shows both through `Views`. The look of the blocks is not final. Change it later in `view.json` and in the views, with no change to the system.

### Open questions

- The detailed design review of the blocks (labels, order, wells, notice and ack titles) comes later. It does not stop the integration child.

## R2 result: views on the render model (2026-11-17)

Branch `orch/session-662a3f1d`: I1 (`orch/integration-t1-q1`) with a merge of R1 (`orch/session-d7cf20ac`). The PR base is `orch/integration-t1-q1`.

### What renders from `view.json`

| View | Component | Model | Parts |
|---|---|---|---|
| Open | `Pages/Open.razor` with `Ui/DocBody.razor` | `Board.Document(e)` → `Views.Document` | title, subtitle, facts table, blocks in the order of the view |
| Card in the pile | `Ui/CardPreview.razor` | `CardView.Preview` → `Views.Preview` | title, pill, well (else line), list, progress, chips with the marked default |
| Stream row | `Ui/EventRow.razor` | `Board.Preview(e)` → `Views.Preview` | title, line, pill, progress |

- The hand-made lists are gone: `Open.razor` `Rows` and `CardPreview.razor` `Well()` and its plan steps.
- Each look of a block has one case in `DocBody.razor`: text, code, well, list, steps, chips, links. `FactValue.razor` shows a fact or a pill.
- A kind with no template (for example `blocked`, `pr.open`) has no `view.json`. `Board` makes a model with each field as a text or list block.
- These parts stay as they were: the card actions and labels by sub-type (`Ui.Classify`), the head label "Decision" (`Ui.KindLabel`), the ring icon and color (`Ui.Look`), the status pill of the row (waits, answered, sent), the default timer ring and the Result list of Open.

### Test results

| Test | Result |
|---|---|
| `dotnet test --solution Orchestrator.slnx` | 297 pass, 1 skip, 0 fail |
| `RenderViewTests` (bUnit) | 31 pass: Open, card and row for each of the 10 kinds with `templates/<kind>/example.json`, and the fallback for a kind with no view |
| Tray self-test (`tray.ps1 -SelfTest`) | 23 of 23 PASS. New: `open-question`, `open-report`, `open-notice`, `row-notice`, `card-question`, `card-report` |

Screenshots (mock mode, desktop corner): `docs/screens/r2-corner-open-question.png`, `r2-corner-open-report.png`, `r2-corner-open-notice.png`, `r2-corner-card-question.png`, `r2-corner-card-report.png`, `r2-corner-row-notice.png`.

### Blocks that look different (accepted, the user tweaks them later)

| Kind | Before | Now |
|---|---|---|
| question | Rows: Question, Context, Snippet, Recommendation, Options, Default, Risk, each in its own card | Facts table: Risk (pill), Default. Blocks: Context, Snippet (code), Options (chips, default marked), Recommendation (well in a card) |
| question (no risk) | Well "Why" on the card | Well "Context" |
| answer to review (card) | Title: the question. Well "Applied": text and reason | Title: the answer text. Well: Reason, else Source |
| report | State not shown on Open | State pill in the facts table and on the card |
| notice | Rows of each field | Title: first line of the render. Facts: Kind, Session, Child. Block: full render text. The title repeats the first line of the block |
| stream row | Line 2: the envelope subject | Line 2: preview title. Line 3: preview `line`. Pill of the view next to the status pill. Progress bar for an update |
| links | Plain list | Links open in a new tab and break at any character |

### Findings and notes

- R2-1: a notice goes to a session, never to the user inbox. So a notice has no card in the pile. The screenshot shows its stream row (`r2-corner-row-notice.png`).
- R2-2: the mock store now has a report with done, next and links, and a `question-closed` notice. The self-test needs them for the render screenshots.
- R2-3: the well block of Open is a well in a card. It shows a double frame. Accepted for the later block review.
- R2-4: `Ui.Look` and the `icon` and `tone` of `view.json` overlap. The ring still uses `Ui.Look`, because it also covers kinds with no template. Open question for the block review.

## S1 design: session registry (2026-11-17)

Page: `docs/session-design.html` (https://claude.ai/artifact/QKh7x9SkrnUp8q5E4rbYTs). Design only: no code change.

Each session is one entry in a session registry, `store/sessions/<id>/session.json`. The registry is one more storage port (`ISessionRegistry`) on the H1 rules. Only the registry service in C# writes it; a plugin posts `session.update`. The run state (lease, heartbeat, status, turn, `state.json`) stays in its own files.

| Part | Design |
|---|---|
| Names | Id (today the Claude Code id, behind the engine adapter), code (`S1`), title. The full code is the codes from the root down; the app title is `[O2 P1 S1] <title>` |
| Entry | `id`, `code`, `engine` (kind, `sessionId`, `appId`: references only), `parent`, `title`, `owner`, `tags`, `stream`, `work`, `role`, `settings`, `lifecycle` (phase and a copy of the B2 state), `created`, `updated`. Children are a query |
| Replaces | `meta.json` and `control.json` (store version 3) |
| Engine first | If the engine gives a function (attach, title, Remote Control, archive, transcript), the adapter uses it. The registry keeps no copy of an engine fact; `adapter.Read` gets it on demand |
| Adapter | The direction decides. Code that touches a model or a model session goes through `IEngineAdapter`: `Create`, `Attach`, `Open`, `Turn`, `Read`, `Set`, `Archive`, `Ask` (one-shot call for the docs and state checks). A call into our system (start the tray, write a record, post an envelope) needs no adapter, whoever makes it. Today only the Claude Code adapter |
| Attach | Each adapter has its own attach. Claude Code: the official attach (`start_session`, app parent link and parent control) when the turn permits it (F1), else create and import. Codex later: its own attach, not in the Claude app. The registry records the parent for all engines |
| Store root | The registry, the run state and the event stream stay under one store root (`ORCH_STORE`, else `<orch home>/store`). `StoreBackends.Create(root)` makes the three ports. Engine data stays in the engine |
| Settings | Each setting can be set at create and updated by the parent or the user. A setting not set at create is a copy of the parent value; a root copies `policy.json` `sessionDefaults` |
| Modes | A mode is a preset of four axes: `interaction` (task: one report at the end; stepwise: a report after each step, no confirmation; conversation: ends when somebody says done), `autonomy` (auto, ask, approve), `route` (parent, user), `visibility` (public: import and Remote Control at the earliest point; private: neither). `visibility` replaces `background`. Presets in `templates/modes/`: `auto`, `chat`, `ask`, `approve` (the sum test modes) |
| Report rules | `reportOn` (done, blocked, asked). The plugin changes a `working` report with no status request into an `update` (sum test F5) |
| Loop | The table of who sends what is on the Loop tab. A chat line closes a decision only with `answer:` (F2, PR #27) |
| Other engines | `IEngineAdapter` with capabilities. A tool joins with `orch session join`, takes the lease, and uses `orch watch` and `orch post` with the same envelopes |

Code parts (proposal):

| # | Part | Size |
|---|---|---|
| C1 | `ISessionRegistry`, file implementation, contract tests | Medium |
| C2 | Store version 3: `meta.json` and `control.json` into `session.json` | Small |
| C3 | Settings schema, copy at create, `mcp__orch__configure`, the settings block in the brief | Medium |
| C4 | `templates/modes/` and the readers of the four axes | Medium |
| C5 | `IEngineAdapter` (create, attach, open, turn, read, set, archive) with the Claude Code adapter. No other code calls an engine | Medium |
| C6 | `orch session` CLI, `/api/sessions` fields, codes and app titles (`set_session_title`) | Small |
| C7 | `orch session join` for other engines, with lease and heartbeat | Small |

C5 scope: the Claude Code coupling in the code today (review of 2026-11-17).

| Place | Coupling | Move to |
|---|---|---|
| `Store/Lifecycle.cs` | `OpenCommand` (`claude.exe --desktop --resume`), `Import` (`local_<id>`), the archive wait | Adapter `Open`, `Archive` |
| `Store/AppRecords.cs`, `StateReader.cs` | Reads the internal app files (`Claude-Data/*/claude-code-sessions/*.json`, `isArchived`) | Adapter `Read`. It stays a file read: the supervisor process has no app tool |
| `Core/Contract/Records.cs`, `States.cs`, `Policy.cs` | `Imported`, `RemoteControl`, `AppId`, `Background`, `RemoteControlAsked`, `RemoteControlSeconds` in the core contract | `engine` references and adapter facts; the policy value goes to the adapter options |
| `Cli` `SessionCommands`, `EventCommands`, `ToolCommands` | `CLAUDE_CODE_SESSION_ID`, the `doctor` checks of `claude` and `~/.claude/settings.json` | An adapter method `CurrentSessionId()`, a doctor check for each adapter |
| `Supervisor/ClaudeRunner.cs` | `ClaudeRunner`, `ClaudeDocsChecker`, `ClaudeStateChecker` call `claude -p` directly | `ClaudeRunner` becomes adapter `Turn`; the two checkers keep their ports and call adapter `Ask` |
| `templates/*/render/*.md` | The text names `mcp__orch__*` tools and `orch:` skills | A render for each engine, or tool names from the adapter |
| `plugin/` | The live side in Claude Code | Stays: it is the Claude Code part of the adapter |

Open: a probe of `set_session_title` from a child and from the parent; the first engine after Claude Code; a team owner.
