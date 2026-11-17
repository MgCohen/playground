# Store contract

All communication goes through one event stream in the store. A sender posts a body file and an envelope. Each party gets a push for each envelope to its address, then queries and reads what it needs. The environment variable `ORCH_STORE` changes the store root.

## Layout

```
store/
  version.json                       { version: 2, at }
  policy.json                        the policy in use
  events/<day>/<id>.json             envelopes, append only, one file each
  bodies/<day>/<id>.json             bodies, write once
  subscriptions/<party>/<name>.json  { party, name, cursor, recent, at, filter } of one subscriber
  nudges/<party>                     a nudge: the write time is the signal, the text is an envelope id or empty
  claims/answer/<thread>             the claim of the first answer in a thread: an exclusive create, the text is the sender
  sessions/<id>/                     the lifecycle files of one session (below)
  supervisor/log.txt                 one line for each turn, relay, default, nudge and blocked delivery
```

A `<party>` in a file name has `-` in place of `:`, because Windows refuses `:` in a file name: `session:<id>` becomes `session-<id>`.

There is no other communication file. The inbox, the answers, the reviews, the reports and the open decisions are queries of the stream. A reactor can keep a cache if it can make the cache again from the stream (for example `docs-checks/`).

## Envelope

```json
{
  "id": "1763300000000-a1b2c3d4-cli", "at": "2026-11-16T10:00:00.000Z", "session": "<session id>",
  "kind": "decision", "from": "session:<id>", "to": "user",
  "subject": "Which color do we use in color.txt?", "body": "bodies/2026-11-16/1763300000000-a1b2c3d4-cli.json",
  "thread": "toolu_01RAQ", "needs": "answer",
  "refs": { "reply": "<id>", "of": "<id>", "via": "session:<root> | chat" }
}
```

| Field | Use |
|---|---|
| `id` | `<ms>-<8 hex>-<writer>`. The ids of one writer always grow. The writer is `supervisor` (the supervisor, or the plugin in a supervisor turn), `cli` or `live` |
| `session` | The session that the envelope is about |
| `from`, `to` | `user`, `supervisor`, `docs`, `policy` or `session:<full id>`. One receiver for each envelope |
| `subject` | One line of 80 characters maximum. One rule in `Subjects.Of` (C#) and `subject` (`stream.ts`); the contract tests check both |
| `body` | The path of the body. A forward points to the body of the original |
| `thread` | Groups one conversation. For a decision, a query or a plan it is the tool call id; the answer, the review and the notices use the same thread |
| `needs` | `answer`, `review` or `ack`: the receiver must act. Absent: nothing to do |
| `refs.reply` | The envelope that this envelope answers. It closes that envelope |
| `refs.of` | A forward (same body, new receiver) or an ack: the original envelope |
| `refs.via` | The relay that typed the words of the user: `session:<root>` for `mcp__orch__answer`, `chat` for a chat line |
| other `refs` | Traces: `turnStart`, `envelopes`, `row`, `tool`, `output`, `parent`, `envelope` (of `delivery.blocked`) |

## Primitives

| # | Primitive | C# (`EventStream`) | CLI | HTTP | Plugin (`stream.ts`) |
|---|---|---|---|---|---|
| 0 | Setup | `Open()` | `orch init` | None | Folders on the first write |
| 1 | Post | `Post`, `Forward`, `Ack` | `orch post <kind> --to P`, `orch send`, `orch answer`, `orch review` | `POST /api/stream` | `post`, `ack` |
| 2 | Subscribe | `Subscribe(party, name, filter)`, `StreamWatcher` | `orch watch --to P`, `orch events --follow` | `GET /api/stream/live` (SSE) | A child `orch watch` |
| 3 | Query | `All`, `After`, `Thread`, `RepliesTo`, `AcksOf`, `ForwardsOf` | `orch events` | `GET /api/events` | `orch watch --list` |
| 4 | Read | `Body`, `Fields`; `Renderer.Text(e, name)` for the receiver | `orch read <id> [--for P] [--render N]` | `GET /api/events/<id>?for=P` | The `text` of each `orch watch` line |
| 5 | Nudge | `Nudge(to, id)` | `orch nudge <party> [id]` | `POST /api/nudge` | `session.send` peer nudge, `session.receive` |

## Post rules

1. Post writes the body first, then the envelope. The envelope is the commit point. A body with no envelope is ignored.
2. Envelopes and bodies never change. A correction is a new post.
3. A message kind must match `templates/<kind>/schema.json`. Its `from` and `to` must be in `meta.json`. A `refs.reply` must name an envelope whose type takes this reply type. An error names the type and each wrong field.
4. Post refuses a second answer in one thread, and gives the first answer in the error text. A forward is not a second answer.
5. A forward is a new envelope with a new `to`, the same body, the same `from` and `thread`, and `refs.of`.
6. Before an answer, the writer creates `claims/answer/<thread>` with an exclusive create. If the file exists, the post is refused with the first answer. So the first answer wins also between processes (the supervisor, `orch`, the UI).
7. The plugin refuses an unknown kind, a message kind with no `meta.json`, missing required fields, and a `from` or `to` that `meta.json` does not allow. The engine checks the full schema before the tool call. The plugin never posts an answer or a review: these go through `orch`, so the reply-type and first-answer checks are in C# only.
8. The envelope file holds only the fields in the table above. The C# record has computed helpers (`Of`, `Reply`, `Via`, `IsForward`); they are not in the file.

## Types

Each message type has a folder `templates/<type>/` with `schema.json`, `meta.json` (purpose, from, to, needs, replies, card), `example.json` and `render/<name>.md`.

| Type | From | To | Needs | Replies | Render for a session |
|---|---|---|---|---|---|
| `brief` | user, session, supervisor | session | | report | `render/session.md` |
| `message` | user, session, supervisor | session, user | | message | `render/session.md`: "Message from <sender>:"; no prefix from the user |
| `decision` | session | user; forward to the parent | answer | answer | `render/parent.md`: "Question from child" |
| `query` | session | user; forward to the parent | answer | answer | `render/parent.md` |
| `plan` | session | user; forward to the parent | review | review | `render/parent.md`: "Plan from child" |
| `report` | session | user; forward to the parent | ack (done, blocked) | ack, message | `render/parent.md`: "Report from child" |
| `update` | session | user | | | Not delivered |
| `answer` | user, docs, policy | session; forward to the user | review (docs, policy) | review | `render/session.md` + `render/note-*.md`; `render/tool.md` for `ask_user` |
| `review` | user | session | | | `render/<verdict>.md`, `render/plan.md` |
| `notice` | supervisor | session | | | `render/<kind>.md`: `question-reminder`, `question-closed`, `archive-self`, `restart`, and the lifecycle notices (below) |
| `ack` | session, user, supervisor | the sender | | | Not delivered |

Other kinds have no template: traces (`session.input`, `session.output`, `session.tool`, `turn.start`, `turn.end`, `turn.fail`), lifecycle names of stream B (`session.start`, `session.stop`, `session.resume`, `session.archive`, `session.import`, `session.end`, `session.state`, `state.conflict`, `lease.take`, `lease.release`, `blocked`, `delivery.blocked`) and git (`branch.*`, `pr.*`, `merge.block`).

The lifecycle notices of the supervisor:

| Notice kind | To | Sent on |
|---|---|---|
| `child-not-responding`, `child-responding` | Parent | Enter and leave not responding. Fields `fault`, `text` |
| `report-missing` | Session | Enter forgotten: the nudge, one time for each last report |
| `child-forgotten` | Parent | Still forgotten `forgottenMinutes` after the nudge |
| `child-stopped`, `child-resumed` | Parent | Enter and leave stopped |
| `child-archived` | Parent | The archive committed |
| `child-archived-in-app` | Parent | The user archived the session in the app sidebar, and the store side is not archived |
| `archive-reminder` | Parent | Done for `reminderMinutes`, no open PR, not archived. One time |
| `remote-control-self` | Session | After the import into the app, unless `meta.background` (F4) |
| `remote-control-child` | Parent | `remoteControlSeconds` after `remote-control-self`, and `meta.remoteControl` is not set. Field `appId` |
| `archive-child` | Parent | An archive request reached its deadline and the app still shows the session (F8, path 2). Field `appId` |
| `state-inferred` | Parent | The fallback agent inferred the state. Fields `state`, `text` |

A root session has no parent: each notice to the parent goes to the user as a `message` from `supervisor` with `needs: ack`. Its text is the render of the notice.

## Subscriptions and delivery

| Subscription | Party | Filter | Who moves the cursor |
|---|---|---|---|
| `subscriptions/session-<id>/deliver.json` | The session | `to = self`, a deliverable kind | The supervisor and `orch watch`, after each ack |
| `subscriptions/supervisor/relay.json` | The supervisor | decision, query, plan, and a report with `needs` from a session | The supervisor, after the forward |

- A subscriber waits for a push of `StreamWatcher` (a file watcher on `events/` and `nudges/`). It also queries from its cursor after `delivery.sweepSeconds` with no push, because a file watcher can drop events.
- `Next()` gives the matching envelopes after the cursor that the subscriber did not mark done. Envelopes of other writers can arrive a little late, so the scan starts 120 s before the cursor, and the file keeps the done ids of these 120 s in `recent`. Limit: an envelope that appears more than 120 s after its id time can be missed by a subscription.
- The delivery cursor moves only over acknowledged envelopes before the first open one. It never passes an envelope that waits for its ack.
- The deliverable kinds are brief, message, decision, query, plan, answer, review, report and notice. A report is deliverable only with `needs` or as a forward.
- Each delivered envelope gets an ack: `{ kind: ack, from: <receiver>, to: <sender>, refs.of, body: { state: delivered | read, by } }`. `by` is `live`, `supervisor`, `tool-result`, `chat` or `user`.
- The envelopes to a session with no ack are its pending deliveries. The ack is the truth; the cursor only marks where the scan starts.

| Path | Who delivers | How |
|---|---|---|
| Live session (lease `live`) | The plugin | A child `orch watch --to session:<id>` prints one JSON line for each new envelope. When the session is idle, the plugin runs `orch watch --list`, submits the texts as one turn, and posts an ack (`by: live`) for each |
| `ask_user` with `waitSeconds` | The plugin | `orch watch --kind answer --thread <id> --once --timeout <s> --render tool`. The tool result is the answer, and the ack has `by: tool-result` |
| Chat line | `orch chat-answer` | Only with one open decision: an answer with `via: chat` and an ack with `by: chat` |
| Session that is not live | The supervisor | One `claude -p` turn with the texts joined by `---`. `turn.start` keeps the envelope ids in `refs.envelopes`, not the prompt. After a good turn, an ack (`by: supervisor`) for each |

## Watchdog

| Step | Receiver state | Action | Wait (`policy.json`, `delivery`) |
|---|---|---|---|
| 1 | Live and idle, no ack | `Nudge(session:<id>)`: `orch watch` looks at the stream again | `nudgeSeconds` (15) after the envelope or the last idle |
| 2 | Still no ack | `delivery.blocked` to the user (`needs: ack`, a card) and to the sender | `peerSeconds` (60) more |
| 3 | Not live | The turn runner delivers. After `MaxFailures` failed turns: `delivery.blocked` | |

The watchdog does not count the time while the live session runs a turn. A session sends a peer nudge (`$.session.send` with `[orch nudge]`) after a message to another session; the receiver consumes it in `session.receive`. The supervisor sends no peer nudge.

## Reactors

| Reactor | Party | Reaction | Posts |
|---|---|---|---|
| Session deliverer | Plugin, holder `live` | Submits a turn | ack |
| Turn runner | Supervisor | `claude -p` with the renders | `turn.*`, ack |
| Ask wait | Plugin, `ask_user` with a wait | Returns the tool result | ack |
| Question relay | Supervisor | Forwards each decision, query and plan of a child to its parent at once | forward |
| Report relay | Supervisor | At the end of a turn (the session does not run), forwards the newest done or blocked report of a child to its parent. An older report of the same turn with no forward gets an ack `{ state: read, by: superseded }` from the supervisor (F5) | forward, ack |
| Lifecycle | Supervisor | One step of the state machine for each session at each tick (below) | `session.state`, `state.conflict`, `lease.release`, notices |
| Question follow | Supervisor | A closed notice when the question of a child closes; reminders while it waits with no default (10 min, then each hour) | notice |
| Docs check, default | Supervisor (`docs`, `policy`) | An answer to the session, and a forward to the user with `needs: review` | answer, forward |
| User inbox | UI, `orch inbox`, `GET /api/inbox` | Shows a card | answer, review, ack, message |
| Watchdog | Supervisor | Nudge, then `delivery.blocked` | `delivery.blocked` |

### User inbox

```
open cards = envelopes to = user with needs (or a decision or query with no answer)
             minus envelopes with a reply (refs.reply) or an ack (refs.of)
             minus a report with needs ack whose forward has an ack (the parent got it)
             the newest envelope of each thread
```

| Card | Accept posts | Refuse posts |
|---|---|---|
| decision, query | answer = `defaultOption` (or the text) | answer = the text |
| answer by docs or policy | review approve | review refuse with the text |
| report done or blocked, `delivery.blocked` | ack (`state: read`) | message with the text to the session, then ack |

## Session files

The plugin and the supervisor share one folder for each session: `store/sessions/<session id>/`. These files are lifecycle state; stream B owns their rules.

| Path | Writer | Content |
|---|---|---|
| `enabled` | Supervisor, `orch enable`, `/orch-enable` | Empty file. The plugin does nothing in a session without it |
| `lease.json` | Plugin, supervisor | `{ holder, since, reason, pid, started }`. `holder` is `live`, `supervisor` (from `ORCH_HOLDER`) or `none`. `pid` and `started` are the id and the start time of the claude process of the session: the plugin gets them from `orch process` (the parent of the `orch` process). The supervisor writes only `none`, when it releases a stale lease |
| `heartbeat.txt` | Plugin | ISO time, at activation and every 3 s while the process is alive |
| `status.json` | Plugin | `{ state: running \| idle, turnId, at }` for the main loop |
| `mirror/<ms>-<uuid>.json` | Plugin | `{ door, origin, uuid, message }` for the doors `prompt`, `response` and `tool-result`. `message` is JSON, cut to 4000 characters |
| `log-<holder>.txt` | Plugin | Log lines of one holder (`live` or `supervisor`), cut to the last 100 000 characters |
| `meta.json` | Supervisor, `spawn_child`, `orch new`, `orch enable`, `/orch-enable` | `{ id, parent, title, cwd, createdBy, createdAt, started, worktree, agent, skills, repo, branch, allowDefaults, imported, appId, background, remoteControl }`. `imported: true` after the app confirmed an import. `appId` is the id of the session in the app. `background: true` (from the brief) keeps Remote Control off. `remoteControl` is `true` or `false` after the session called `set_remote_control` on itself (the plugin records it). `allowDefaults: true` (only from the user) lets `orch merge` carry unapproved defaults. With `worktree: true`, the first turn makes a git worktree: `cwd` becomes the worktree folder, `repo` the repository and `branch` its branch. A session with `createdBy` and no `started` gets a new `claude -p` session with this id |
| `blocked.json` | Plugin, supervisor | `{ reason, since, by }`. `reason` is `null` when the session is not blocked. `by: supervisor` for failed supervisor turns; no `by` for the stop latch that the plugin found. The plugin also posts `blocked` at the stop latch |
| `state.json` | Supervisor | The lifecycle state: `{ state, since, requested, inferred, guard, fault, waitReason, reminded, remoteControlAsked, history }` (below) |
| `state-check.json` | Supervisor | `{ state, reason, at, error }`: the last answer of the fallback agent `orch:state-checker` |
| `forgotten.json` | Supervisor | `{ report, nudged, notified }`: the nudge of a forgotten session for its last report id, and the notice to the parent |
| `docs-checks/<id>.json` | Supervisor | `{ answered, answer, source, at }`: the cache of the docs check of one decision |
| `turn.json` | Supervisor | `{ event, from, at, ended }`: the `turn.start` of the last supervisor turn, its senders, and the end time. The plugin uses `from` as the sender of `session.input` |
| `last-output.json` | Plugin | `{ event, at }`: the last `session.output`. `turn.end` links it in `refs.output` |
| `stack/<decision>.json` | `orch decision`, `orch stack` | The versions of one decision on the stack (below) |
| `control.json` | `orch stop`, `orch resume`, `orch archive`, the API, the supervisor (auto-archive) | `{ state: active \| stopped \| archived, at, by, reason }`. The supervisor runs no turn of a stopped or archived session, and ends its running turn. The plugin delivers no envelope while the state is not `active` |

## Lifecycle state machine

The code is in `Orchestrator.Core/Lifecycle` (no IO) and `Supervisor.Lifecycle.cs`. The design is `docs/lifecycle-design.html`. At each tick, the supervisor reads a snapshot of each session and runs one step. It writes `state.json`, and posts `session.state` when the state, the request or the escalation changes.

| State | Detect check (no `state.json`) |
|---|---|
| `archived` | `control.json` archived, or no `enabled` |
| `stopped` | `control.json` stopped |
| `stale` | Lease `live`, heartbeat and lease older than `staleSeconds` |
| `not-responding` | One fault: failed turns, stop latch, input not taken, hung turn, hung process |
| `start` | `meta.started` is not true, and no turn runs |
| `running` | A supervisor turn, or `status.json` running with a fresh heartbeat |
| `done` | The last report is done, and no input arrived after it |
| `forgotten` | Idle for `forgottenMinutes`, last report not done or blocked, no wait reason, no input after the turn |
| `idle` | No turn |

The checks run in this order, and the first yes wins. With a `state.json`, only the transitions that leave the current state are checked. The four global transitions come first: archive, stop, stale, fault, each only from a state with a higher rank. Then the local transitions, by priority. Two local guards with the same priority are a conflict: the state stays, `state.conflict` is posted, and the fallback agent runs. A `state.json` that is not valid also goes to the fallback agent. An inferred state has `inferred: true` and starts no action.

`state.json`:

```json
{ "state": "idle", "since": "<time>", "guard": "turn-end", "waitReason": "none",
  "requested": { "to": "archived", "transition": "archive", "since": "<time>", "deadline": "<time>", "waitsFor": ["enabled gone", "app archived"], "escalations": 0 },
  "history": [ { "transition": "forgotten", "at": "<time>" } ] }
```

| Field | Meaning |
|---|---|
| `requested` | An open requested transition: stop, archive or auto-archive. The state stays until the signals in `waitsFor` arrive. At `deadline` the request escalates. A stop tells the parent. An archive asks the parent to archive the app session (`archive-child`), then after `archiveUserMinutes` the user |
| `waitReason` | `decision`, `child`, `blocked` or `none` |
| `history` | The transitions of the last `loopMinutes` that count for the loop rule. The main path (start, running, idle, done) does not count. A transition that ran `loopCount` times in the window gives `not-responding` with fault `loop` |

| Action | When |
|---|---|
| Release the lease (`lease.release`) | `stale` to `idle` because the process in `lease.json` is gone. A lease with no `pid` counts as gone, as before B2 |
| No supervisor turn | While the lease holder is `live`, also with an old heartbeat |
| Notice to the parent | See the lifecycle notices above |
| Store side of the archive | An `auto-archive` request, when the app archived the session or the session is not in the app |

## Decision, answer and review

A decision is the envelope `decision` from a session, with `thread` = the tool call id and the decision template as body. It is open while its thread has no answer that is not a forward.

| Read | Query |
|---|---|
| `Decision(thread)` | The first decision or query in the thread that is not a forward |
| `Answer(session, thread)` | The first answer in the thread that is not a forward. `by` is `user`, `docs` or `policy`; `note` is the render of `answer/render/note-*.md` |
| `Review(session, version)` | The newest review in the thread with `version` = the version id |
| `Report(session)` | The newest report from the session |

| `ask_user` | What the plugin does |
|---|---|
| `waitSeconds: 0` | Returns PENDING. The answer arrives later as a turn "Answer to decision <id> (<question>): <text>" |
| `waitSeconds > 0` | Waits up to that time (580 maximum) for the answer; the tool returns `ANSWER: <text>`. Else PENDING |

## Stack

`stack/<decision>.json` keeps the versions of one decision. `orch decision start` adds a version, `orch decision pr` opens its PR, and `orch stack close` closes a version and plans the next one.

```json
{ "decision": "toolu_01", "session": "<id>", "versions": [
  { "version": 1, "branch": "orch/decision-toolu_01", "base": "main", "pr": 2, "state": "closed" },
  { "version": 2, "branch": "orch/decision-toolu_01-v2", "base": "main", "replaces": 2, "state": "planned" } ] }
```

| State | Meaning |
|---|---|
| `planned` | `orch stack close` made it. `orch decision start` checks out its base and makes its branch |
| `started` | The branch exists |
| `open` | A draft PR exists |
| `ready` | The user approved it. The PR is ready for review |
| `closed` | The user refused it, or a version below it. The branch stays |
| `merged` | `orch merge` merged a branch that carried this version |

A version id is the decision id for version 1 and `<decision>-v<n>` after it. A review applies to the newest version that is not `planned`; its body keeps the version id.

## HTTP API (supervisor, `http://127.0.0.1:7777`)

| Call | Body | Result |
|---|---|---|
| `POST /api/stream` | `{ kind, from?, to, body, thread?, needs?, reply?, via? }` | The envelope (201). 400 with the field errors |
| `GET /api/stream/live` | Query: `after`, `to`, `kind` | Server-sent events: one `data:` line with the envelope for each new envelope |
| `GET /api/events` | Query: `since` (`3h` or an ISO time), `after` (an id), `kind` (comma list), `session`, `from`, `to`, `thread`, `needs`, `open`, `ledger` | The matching envelopes in order |
| `GET /api/events/<id>` | Query: `for`, `render` | `{ envelope, body, text }`. `text` is the render for the receiver |
| `POST /api/nudge` | `{ to, id? }` | `{ nudged }` |
| `GET /api/inbox` | | The open cards: `{ id, kind, needs, session, title, envelope, fields }` |
| `POST /api/inbox/<id>/accept` | `{ text? }` | The posted envelope |
| `POST /api/inbox/<id>/refuse` | `{ text }` | The posted envelope |
| `GET /api/sessions` | | Each enabled session: title, parent, holder, live, running, status, report, blocked, open decisions, and from `state.json`: `state`, `waitReason`, `requested`, `fault`, `inferred` |
| `GET /api/decisions` | | All open decisions |
| `POST /api/sessions` | `{ title, cwd, brief, parent?, background? }` | `{ id }`. A brief to the new session |
| `POST /api/sessions/<id>/send` | `{ text }` | `{ event }`. A message from the user |
| `POST /api/decisions/<session>/<id>` | `{ answer }` | `{ answered }`. An answer from the user |
| `POST /api/decisions/<session>/<id>/review` | `{ verdict: approve \| change \| refuse, text }` | `{ event }`. A review to the session |
| `GET /api/ledger` | | The answers by docs or policy, and each review |
| `POST /api/sessions/<id>/stop`, `/resume`, `/archive?force=true` | | Lifecycle (stream B) |

## Questions to the user

The user answers in free text, never with a click. The `options` of a decision are suggestions only.

| Layer | What it does |
|---|---|
| `~/.claude/settings.json` | `permissions.deny: ["AskUserQuestion"]` removes the built-in question tool from all sessions |
| Plugin, fallback | `tool.describe` moves `AskUserQuestion` behind ToolSearch with "Do not use this tool". `tool.call` refuses it: in a normal session the model asks in its reply text; in an orchestrator session it calls `ask_user` |

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
  "onDefault": "stacked-pr",
  "delivery": { "nudgeSeconds": 15, "peerSeconds": 60, "sweepSeconds": 60 },
  "lifecycle": { "staleSeconds": 15, "staleProcessSeconds": 120, "forgottenMinutes": 15, "inputSeconds": 120, "hungMinutes": 30,
                 "stopSeconds": 30, "archiveSeconds": 120, "archiveUserMinutes": 10, "reminderMinutes": 60, "remoteControlSeconds": 120,
                 "loopCount": 3, "loopMinutes": 60 },
  "autoArchive": { "enabled": false, "afterMinutes": 60 }
}
```

| Field | Meaning |
|---|---|
| `levels` | Any number of levels, from the least strict to the most strict. The model picks one for each decision from the `description`. An unknown level counts as the last level |
| `timeoutSeconds` | After this time with no answer, the policy answers with `defaultOption`. `null`: never. A decision with no `defaultOption` always waits |
| `checkDocs` | A read-only check first looks if the `docs` paths in the session folder answer the question. If yes, that answer applies at once, with `by: docs` and the source |
| `onDefault` | What a default answer tells the session to do (`answer/render/note-default-<onDefault>.md`): `stacked-pr`, `commit` or `none` |
| `delivery` | The wait times of the watchdog and the sweep of each subscriber |
| `lifecycle` | The timers of the state machine. An absent field takes the default above |
| `autoArchive` | Off by default. On: the supervisor requests the archive of a done session with no open PR, no open decision and no input for `afterMinutes` |

A review of a default decision (`templates/review/render/<verdict>.md`):

| Verdict | Instruction to the session |
|---|---|
| `approve` | `orch decision ready <id>`: mark the PR ready for review. Do not merge |
| `change` | Change the decision branch and commit. `orch stack rebase <id>` rebases and pushes each branch above it |
| `refuse` | `orch stack close <id>` closes the PR and each PR above it, keeps the branches and plans a `-v2` version of each. `orch decision start` and `orch decision pr` make the new versions in the direction of the user |

### Merge

`orch merge <pr>` finds each decision version whose branch tip is in the head of the PR and not in its base. With no GitHub remote, use `orch merge <branch> --base <base>`.

- The command refuses the merge while a carried version has an answer by `policy` and no `approve` review. A `change` review also blocks.
- `allowDefaults` on the session or on a parent permits the merge.
- A refusal writes one `merge.block` envelope for each blocker. A merge writes `pr.merge` and sets each carried version to `merged`.
- The plugin denies `gh pr merge` in Bash and PowerShell in orchestrator sessions.

## Rules

1. Write each file in one write: a temporary file, then a move.
2. Only one writer has the lease. The plugin delivers only with holder `live`. With any other holder, the supervisor delivers with `claude -p`.
3. A lease `live` counts as live while the heartbeat or the lease is less than 15 s old. After that, the session is `stale`. The supervisor releases the lease (`none`) only when the process in `lease.json` is gone. It never runs a turn while the holder is `live`. If the process is alive with no beat for `staleProcessSeconds`, the session is `not-responding`. The plugin takes a released lease back at its next beat.
4. The supervisor and each `orch` command (except `doctor` and `template`) refuse a store with another version. The plugin stays inactive in a store whose `version.json` is not version 2. There is no migration from version 1: move the old store to another folder and start with a new store.

## Recovery

The supervisor can stop at any moment, for example in a crash. At each tick, it examines the `turn.json` of each session that has no running turn:

| Case | Action |
|---|---|
| `ended` is set | None |
| No `ended`, and the lease holder is `supervisor` with a heartbeat less than 15 s old | Wait: the old `claude` process still runs |
| No `ended`, and a `session.input` has `refs.turnStart` of this turn | The prompt arrived: an ack for each envelope in `refs.envelopes` of `turn.start`, and `turn.end` with `recovered: true` |
| No `ended`, and no such envelope | The prompt did not arrive: `turn.fail` with `recovered: true`. The envelopes have no ack, so they go in the next turn |
