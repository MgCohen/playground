# W: raw audit reports

The verbatim reports of the three audit agents (code at `c445988`). Read `W.md` first. The item ids (B, P, C, T) are local to each report.

Known faults: in the hooks report, the line numbers of `lease.ts`, `events.ts` and `stream.ts` are too large (subtract 284, 223 and 104, then check), and the total of 17 is 22 (P 12, C 9, T 1).

---

# Report: Web and API

Result: the Web project has 11 bypasses. Every page that shows a session reads side files (`session.json`, `enabled`, `status.json`, `lease.json`, `heartbeat.txt`, `state.json`) or memory, and every page also uses a 10 s poll. Each user action posts an envelope, but stop, resume, archive and new session also write files directly. The card, stream and event queries are clean. I changed no file and did not run the app.

## 1. Feature table

| Feature | Reads (real source) | Writes | Update trigger | Verdict |
|---|---|---|---|---|
| LiveView base (all pages, AppBar) | — | — | push (StreamHub) + 10 s timer + reload after an act | bypass R5 (B1, B10) |
| Home: wait ring, waiting list | `StreamQueries.Inbox`; titles from `session.json` | — | push+timer | bypass R2 (B5) |
| Home: active sessions | `Board.Tree` (B2, B3, B4, B5), `LastActive` query | — | push+timer | bypass R2/R4 |
| Home: activity 24 h | `StreamQuery{Since}` | — | push+timer | OK |
| Cards: pile, chips, filter | Inbox query; `Board.Tree` for the roots | — | push+timer | bypass R2 (B2-B5, through Tree) |
| Cards: accept/refuse/sheet/undo | — | answer / review / ack / message envelopes (`Inbox.cs:39-73`, `Messages.cs`) + `supervisor.Tick()` | reload after act | OK (B9 minor) |
| Cards: skip, filter order | memory of the page only | — | — | OK (UI state) |
| Open `/events/{id}`: document, results | `Events.Find`, thread, replies, acks; `view.json` (config) | — | push+timer | OK |
| Open: accept/refuse/send | same as Cards | envelopes | reload after act | OK |
| Stream page, EventRow, KindRing | Timeline query, `IsOpen`, `IsAcked`; Tree for lock/roots | — | push+timer | OK except Tree (R2) |
| Sessions: tree, search | `Board.Tree` | — | push+timer | bypass R2/R4 |
| Sessions: New session form | — | `session.json` + `enabled` + `brief` envelope | reload after act | bypass R3 (B7) |
| SessionStatus: hero, pill, children | Tree: `status.json`, memory `running`, `session.json` phase | — | push+timer | bypass R2/R4 |
| SessionStatus: Holder row | `lease.json` + `heartbeat.txt` | — | timer only | bypass R2/R5 |
| SessionStatus: counts, links | Inbox, `StreamQuery{Session}` | — | push+timer | OK |
| SessionStatus: message | — | `message` envelope | reload after act | OK |
| SessionStatus: stop/resume/archive | — | envelope + `session.json` phase + `enabled` delete + git worktree remove | reload after act | bypass R3 (B6) |
| Settings | `Board.StoreRoot`, Tree count | — | push+timer | bypass R2 (minor) |
| API `GET /api/sessions` | `session.json`, `enabled`, `lease.json`, heartbeat, memory, `status.json`, `state.json` | — | request | bypass R2/R4/R7 |
| API `GET /api/decisions` | OpenDecisions query + `enabled` + `session.json` stream | — | request | bypass R2/R7 |
| API `stream`, `events`, named views, thread, `events/{id}`, `inbox`, `ledger` | StreamQuery | — | request | OK |
| API `GET /api/stream/live` (SSE) | StreamQuery after a cursor | — | push + 15 s keep-alive | OK |
| API `POST stream`, `send`, `inbox accept/refuse`, `review`, `answer` | — | envelopes + `Tick()` | — | OK (B9 minor) |
| API `POST stop/resume/archive`, `POST /api/sessions` | — | as in the UI | — | bypass R3 (B6, B7) |
| API `POST /api/nudge` | — | `nudges/<party>` | — | allowed (plumbing) |
| Store API `/api/store/*` | repositories | any repository (transport) | — | allowed (transport); the real writers are outside scope |
| TickService | `Supervisor.Tick` reads side files | `state.json`, `lease.json`, `turn.json`, `forgotten.json`, `docs-checks/` | push + 5 s sweep | bypass R8 (B8) |

## 2. Bypass list

**B1. Poll timer on every view**
- Where: `Components/LiveView.cs:9,22`
- Rule: R5
- Problem: a 10 s `Timer` reloads every page and AppBar, because side files make no push. `StreamWatcher` watches only `events/` and `nudges/`.
- Fix: delete the timer after B2-B5 are done. Keep a client-side clock tick that only re-renders relative times, with no data load.
- Test: a fake clock plus no envelope gives no `Load()` call.

**B2. Session state from `status.json` and memory**
- Where: `Board.cs:65-66`, `Ui.cs:172`
- Rules: R2, R4
- Problem: `Running` comes from `Supervisor.IsRunning`, an in-memory dictionary (`Supervisor.cs:26,53`). `State` comes from `status.json`.
- Fix: fold the newest `session.state` envelope per session (it exists: `EventKinds.SessionState`). Fold `turn.start`/`turn.end` for running. The plugin must post turn facts, not only write `status.json`.
- Test: an empty store plus the stream replayed gives the same `SessionNode.State`.

**B3. Holder and Live from `lease.json` and `heartbeat.txt`**
- Where: `Board.cs:63-64`, `OrchestratorApi.cs:75`
- Rule: R2
- Fix: fold `lease.take`/`lease.release` for the holder. Liveness comes from `session.state` (the supervisor posts stale). Do not read the heartbeat in a view.
- Test: the holder after a `lease.release` envelope is `none`.

**B4. Phase from `session.json`**
- Where: `Board.cs:67` (`Lifecycle.Control`, `Lifecycle.cs:17`)
- Rule: R2
- Fix: fold `session.start`/`stop`/`resume`/`archive` into the phase projection.
- Test: replay gives the same phase as the registry.

**B5. Session list, titles, parent and cwd from the registry and `enabled`**
- Where: `Board.cs:28,33,58-59`, `Inbox.cs:29`, `OrchestratorApi.cs:87,97,102`, `FileStore.cs:65`
- Rule: R2
- Fix: a SessionDirectory projection that folds `brief`, `session.start` and `meta.update`, and drops the session on `session.archive`.
- Test: titles and the tree from replay are equal to the registry.

**B6. Stop, resume and archive write fields directly**
- Where: `Lifecycle.cs:24,30,49,50,52-53`, called from `Board.cs:116-124` and `OrchestratorApi.cs:121,128,135`
- Rule: R3
- Problem: each action posts the envelope, then calls `Registry.SetPhase`, deletes `enabled`, and runs `git worktree remove` in the request.
- Fix: post `session.stop`/`resume`/`archive` only. One reactor in the supervisor applies the phase, removes the worktree and posts `branch.*` or a notice.
- Test: an action call changes no file outside `events/` and `bodies/`.

**B7. New session writes the registry before the post**
- Where: `Messages.cs:59` → `FileStore.cs:104-107`; called from `Board.cs:105` and `OrchestratorApi.cs:108`
- Rule: R3
- Problem: the action writes `session.json` and `enabled`, then posts `brief`.
- Fix: post `brief` with a new id. A registry reactor creates the entry from the `brief` fold.
- Test: same as B6.

**B8. The sweep writes files**
- Where: `TickService.cs:11,25`; writes at `Supervisor.Lifecycle.cs:36,155,183,355,364`, `Supervisor.cs:190,349,408`
- Rule: R8
- Problem: the 5 s sweep reads side files and writes `state.json`, `lease.json`, `forgotten.json`, `turn.json` and `docs-checks/`.
- Fix: the sweep reads projections and posts `session.state`, `lease.release` and `notice`. Its file caches become folds.
- Test: one tick on a fixture writes only envelopes.
- Note: verified only up to the call sites; the Supervisor is outside scope.

**B9. Actions call `Supervisor.Tick()` synchronously**
- Where: `Board.cs:94,100,106,113,121,166,173,182`; `OrchestratorApi.cs:115,122,129,136,164,200,207,217,224`
- Rules: R3, R5
- Problem: the request thread runs the reactor and its file writes (B8). The post already wakes `TickService` through the push.
- Fix: delete these calls.
- Test: an action returns before any tick runs.

**B10. Reload after an act**
- Where: `LiveView.cs:54`, `Sessions.razor:48`
- Rule: R5 (minor)
- Fix: keep only the toast. The push gives the new data.

**B11. Two folds of session state, two open-decision filters**
- Where: `Board.cs:61-72` (`SessionNode`, state from `status.json`) and `OrchestratorApi.cs:70-84` (`SessionView`, state from `state.json`). Also `Board.cs:31-35` and `OrchestratorApi.cs:100-104` (copied in `Cli/DecisionCommands.cs:13`).
- Rule: R7
- Problem: the UI and the API can show different states for one session.
- Fix: one SessionProjection in Store, used by Board, the API and the CLI.
- Test: the API and the Board give the same state for one fixture.

## Other notes
- `Board.DefaultsToReview` (`Board.cs:41-53`) reads `stack/` (R2), but nothing calls it. It is dead code; delete it.
- The duplicated "roots" maps in `Cards.razor:112-114` and `StreamPage.razor:65-67` are view helpers, not projections. They are low priority.
- Config reads (`view.json`, `Policy` for `DefaultAt`) are out of scope.
- No browser script polls for data. The `setTimeout` calls in `wwwroot` are for animation only.

---

# Report: Store and Supervisor

Audit result: the stream is the source only for messages (question, answer, review, report, forward, ack, failure, notice). The lifecycle facts (lease, heartbeat, status, turn, state, registry changes, workflow runs) live in side files. The supervisor tick reads these files on a 5 s timer. I found 18 bypasses (B1 to B18). I did not change a file. Paths are relative to `session-orchestrator/src/`.

**1. Side file table**

| File | Writers | Readers | Also an envelope? | Rebuildable? | Verdict |
|---|---|---|---|---|---|
| `enabled` | FileStore.cs:73-74 (from :107, Lifecycle.cs:50, :152), plugin `/orch-enable` | FileStore.cs:65,68; StateReader.cs:68; Supervisor.Lifecycle.cs:271; plugin lease.ts beat | Yes: brief, `session.start`, `session.archive` | Yes | Bypass R2 (it repeats `lifecycle.phase`) |
| `lease.json` | plugin lease.ts:51; Supervisor.Lifecycle.cs:183 (FileStore.cs:88) | FileStore.cs:86,92,97; StateReader.cs:34,76; Supervisor.cs:253; Recovery.cs:16; plugin lease.ts:40 | Only the supervisor `lease.release` (Supervisor.Lifecycle.cs:184). `lease.take` (EventKinds.cs:30) has no writer | No | Bypass R1, R2, R7 (two writers) |
| `heartbeat.txt` | plugin backend.ts:102 | FileStore.cs:90 (IsLive :92); StateReader.cs:35; Recovery.cs:16 | No | No | Bypass R1, R2, R5 |
| `status.json` | plugin register.ts:58 | FileStore.cs:280; StateReader.cs:40; Supervisor.cs:214 | No (a live turn posts no turn envelope) | No | Bypass R1, R2, R5 |
| `turn.json` | Supervisor.cs:349,408; Recovery.cs:36 | StateReader.cs:42; Recovery.cs:13; Supervisor.cs:408; plugin lease.ts:40 | Yes: `turn.start` (senders), `turn.end`, `turn.fail` | Yes | Bypass R2, R3 |
| `state.json` | StateReader.cs:27 (from Supervisor.Lifecycle.cs:36,364) | StateReader.cs:16-19 (Supervisor.Lifecycle.cs:29,361), StateReader.cs:97-98 (state of child), Web OrchestratorApi.cs:74 | Partly: `session.state` only on a change. The marks at Supervisor.Lifecycle.cs:123,138,173, `history` and `since` have no envelope | Partly | Bypass R4 (the file is the input of `Machine.Step`) |
| `state-check.json` | Supervisor.Lifecycle.cs:355 | None found | Partly: the `session.state` inferred envelope and the `state-inferred` notice. `error` is not in an envelope | n/a | Bypass R3 (no reader) |
| `forgotten.json` | Supervisor.Lifecycle.cs:155,161; delete :73 | Supervisor.Lifecycle.cs:151 | Yes: notices `report-missing` (:154) and `child-forgotten` (:160) | Yes | Bypass R2, R3 |
| `docs-checks/` | Supervisor.cs:190 | Supervisor.cs:171 (dedupe) | Only a positive result, as an answer (:195) | Partly: a negative result and an error are lost | Cache, R4 partial |
| `last-output.json` | plugin events.ts:59 | Supervisor.cs:376 | Yes: `session.output` (events.ts:58) | Yes | Bypass R2 |
| `session.json` | SessionRegistry.cs:109,128, through FileStore.cs:83,106; Lifecycle.cs:24,30,49,124,136,172; Supervisor.cs:369; Supervisor.Lifecycle.cs:198,224; WorkflowHost.cs:62; plugin `/orch-enable` | Many (FileStore.cs:78) | Partly: brief, start, stop, resume, archive, import, `branch.create`, `meta.update`. Code, started, settings, role, cloud appId: no | Partly | Bypass R1, R4, R7 |
| `runs/` | WorkflowRunner.cs:73,129 | WorkflowRunner.cs:43,47 (CLI) | No run envelope | No | Bypass R1, R4 |
| `stack/` | CLI StackOps.cs:37,72,86,142; MergeGuard.cs:69 | FileStore.cs:192,294,297, so StateReader.cs:88, Lifecycle.cs:38, Messages.cs:148 | Mostly: `branch.create`, `pr.open`, `pr.ready`, `pr.close`, `pr.merge` | Partly. The plan version (StackOps.cs:142) and the merged state of each version (MergeGuard.cs:69) are not verified | R4 partial |
| `mirror/` | plugin backend.ts:106 | None in scope (Web not verified) | Text is in `session.input` and `session.output` | n/a | Trace. Not verified |
| `subscriptions/`, `nudges/`, `claims/` | EventStream | EventStream | n/a | n/a | Plumbing allowed. FileStore.cs:263 moves the delivery cursor inside a read (StateReader.cs:56, Supervisor.cs:206) |

There are also in-memory folds with no rebuild: `failures` (Supervisor.cs:29,413), `nudged` (:31), `imports` (:30) and `fallbackAt` (Supervisor.Lifecycle.cs:18).

**2. Supervisor tick (Supervisor.cs:58)**

TickService.cs:11,25 runs a tick at each push and at least every 5 s. StreamWatcher.cs:20 watches only `events/` and `nudges/`, so a side file change arrives only by the 5 s poll.

| Step | Reads | Writes or posts | Time sweep? | R8 |
|---|---|---|---|---|
| StopRunningTurns :85 | registry phase | cancels the turn | No (reaction) | n/a |
| Relay :100 | subscription `relay` | forward, cursor | No (push) | OK |
| ApplyMetaUpdates (Lifecycle :206) | subscription `meta` | `session.json` | No (push) | OK, the input is an envelope |
| AssignCodes (Lifecycle :194) | registry scan | `session.json` code, no envelope | Yes | Fail |
| StepStates (Lifecycle :25) | `state.json`, `session.json`, lease, heartbeat, status, `turn.json`, `enabled`, `stack/`, child `state.json`, app facts (StateReader.cs:62), stream | `state.json`, `forgotten.json`, `lease.json`, `session.json` + `enabled` (auto-archive :110), cursors; posts `session.state`, `state.conflict`, `lease.release`, notices, forwards, acks | Yes | Fail |
| ApplyPolicy :154 | stream, `docs-checks/` | answer (default); the async docs check writes `docs-checks/` | Yes (default timeout) | Partial |
| FollowQuestions :121 | stream only | notices | Yes (reminders) | OK |
| Watchdog :204 | stream, cursor, lease, heartbeat, `status.json` | nudge, `delivery.blocked`, failure | Yes | Fail (reads side files) |
| StartTurn :263 | `turn.json`, lease, heartbeat, registry, stream | `turn.json`, `session.json` (started, worktree, appId); posts `turn.*`, ack, notices | Partly (backoff :256, import wait :254) | Fail |

**3. Bypass list**

For each bypass: the place, the rule, the problem, then the fix (event kind, projection, test).

- **B1** register.ts:58, read at StateReader.cs:40 and Supervisor.cs:214. R1, R2, R5.
  - Problem: a live turn writes only `status.json`.
  - Fix: the plugin posts `turn.start` and `turn.end` with `by: live`. A TurnProjection holds the open turn of each session. Test: with no `status.json`, the snapshot gives the same `Running`.
- **B2** lease.ts:51 and Supervisor.Lifecycle.cs:183. R1, R2, R7.
  - Problem: two writers, and nobody posts `lease.take`.
  - Fix: post `lease.take` and `lease.release` from both writers. A LeaseProjection holds the holder. Test: delete `lease.json`; `IsLive` does not change.
- **B3** backend.ts:102, read at FileStore.cs:90 and StateReader.cs:35. R1, R5.
  - Problem: the heartbeat is not an envelope.
  - Fix: a `session.beat` envelope at a coarse interval (about staleSeconds/3), folded into the last beat of each session. Alternative: the file stays plumbing, and only the sweep reads it and posts `session.stale` or `session.fresh`. This is an open decision, because of the envelope volume. Test: the stale guard works from the stream only.
- **B4** Supervisor.cs:349,408 and Recovery.cs:13,36. R2, R3.
  - Problem: the turn marker repeats `turn.*`.
  - Fix: `Recovery.Interrupted` becomes "a `turn.start` with no `turn.end` or `turn.fail`" in the TurnProjection. Test: recovery works with no `turn.json`.
- **B5** Supervisor.cs:376. R2.
  - Problem: `turn.end` reads `last-output.json`.
  - Fix: take the last `session.output` from `OfSession(id, start.At)`. Test: `refs.output` is correct with no file.
- **B6** StateReader.cs:16-27 and Supervisor.Lifecycle.cs:123,138,173. R4.
  - Problem: `state.json` is the source of truth, and its marks have no envelope.
  - Fix: post `session.state` for each mark change, or take the marks from the notices `archive-reminder`, `archive-child` and `remote-control-child`. A StateProjection folds `session.state`, and `state.json` becomes a cache. Test: delete `state.json`; the rebuild is equal.
- **B7** StateReader.cs:97-98. R2.
  - Problem: the wait reason reads the `state.json` of each child.
  - Fix: read the StateProjection.
- **B8** Supervisor.Lifecycle.cs:73,151-161. R2, R3.
  - Problem: the forgotten episode is a file.
  - Fix: add an `episode` field to the notices `report-missing` and `child-forgotten`, and query them. Test: the nudge and the notice happen one time each, with no file.
- **B9** Supervisor.Lifecycle.cs:355. R3.
  - Problem: a write-only file.
  - Fix: put the reason and the error in the `session.state` inferred body, then remove the file.
- **B10** Supervisor.cs:171,190. R4.
  - Problem: a negative docs check is not in the stream.
  - Fix: a `docs.check` trace `{decision, answered, source, error}`, and the cache is a fold of it. Test: delete `docs-checks/`; no model call runs again.
- **B11** registry writes with no envelope: Supervisor.Lifecycle.cs:198 (code), FileStore.cs:83 (started, from Supervisor.cs:345,384 and Recovery.cs:32), Supervisor.cs:369 (cloud appId), WorkflowHost.cs:62 (role), `SessionRegistry.Configure` (settings). R1, R4. `/orch-enable` is a second writer (R7).
  - Fix: `SessionRegistry` posts `session.update {field, value, by}` at each write, and the registry is a fold. Test: a rebuild from the stream gives the same entries.
- **B12** WorkflowRunner.cs:73,129. R1, R4.
  - Fix: `run.start`, `run.step` and `run.stop` envelopes, and WorkflowRuns as a fold. Test: a rebuild of the run.
- **B13** MergeGuard.cs:69 and StackOps.cs:142. R4. Not verified fully.
  - Fix: each `SaveStack` posts the full version record. A StackProjection folds it. Test: a stack rebuild.
- **B14** FileStore.cs:65,68; StateReader.cs:68; Supervisor.Lifecycle.cs:271. R2.
  - Problem: `enabled` repeats the phase.
  - Fix: a projection: enabled = a brief or `session.start`, and no `session.archive`. The plugin can keep the file as plumbing.
- **B15** TickService.cs:11,25 (5 s), StreamWatcher.cs:20, and HttpRepositories.cs:78,225-247 (PollSignal every 2 s). R5.
  - Fix: after B1 to B4, the timer drives only the time rules. Give `/api/store` a server push (SSE or long poll).
- **B16** StateReader.cs:62 (`provider.Read` at each tick), Lifecycle.cs:166,187 (poll loops). R1, R5.
  - Problem: the app facts are polled, not posted.
  - Fix: a sweep posts `app.state` when the facts change, and the snapshot reads that projection.
- **B17** Supervisor.cs:29,255,413. R4, R7.
  - Problem: the count of failed turns is only in memory, so a restart loses it.
  - Fix: fold it from `turn.fail` and `turn.end`.
- **B18** FileStore.cs:94-100. R2.
  - Problem: `Cwd` falls back to `lease.reason.start.cwd`.
  - Fix: use `work.cwd` from the registry fold only.

Not verified: the readers of `mirror/` in Web, how complete the stack envelopes are, and the line numbers of the `/orch-enable` write in the plugin.

---

# Report: Hooks, CLI, tray, providers

Result: I found 17 bypasses in total. The plugin hooks keep the session lifecycle in side files. Some CLI commands write the registry, `enabled`, `stack/` and `runs/` directly. The Tray reads the store in process through Orchestrator.Web and has no HTTP or SSE path. The Providers write nothing to the store. I changed no file and ran no write.

## 1. Tables

**Plugin hooks (plugin/hooks)**

| Feature | Reads | Writes | Trigger | Verdict |
|---|---|---|---|---|
| post / ack (stream.ts:188-212) | templates (config), session.json (streamOf :181-185) | bodies/, events/ | tool call, delivery | OK post; bypass R2 (session.json) |
| trace rows (events.ts:263-283) | turn.json (:259) | session.input/output/tool posts; last-output.json (:282) | session.append | bypass R2, R1/R3 |
| mirror (register.ts:61-65, backend.ts:105-107) | none | mirror/<ms>-<uuid>.json | session.append | bypass R1 |
| status (register.ts:56-59, :173, :183) | status.json (:95-96) | status.json | turn.start / turn.complete | bypass R1, R2 |
| lease (lease.ts:323-360, register.ts:100) | lease.json, turn.json | lease.json | activate, beat, session.end | bypass R1, R2 |
| heartbeat (backend.ts:101-103, lease.ts:345, register.ts:97) | none | heartbeat.txt | timer 3 s | bypass R1 |
| enabled check (backend.ts:93-99; lease.ts:339; register.ts:116, :153, :164) | enabled | enabled | timer 3 s, /orch-enable | bypass R2, R3, R5 |
| registry entry (register.ts:122-144) | session.json | session.json (versioned) | /orch-enable | bypass R1/R3 |
| meta.update (register.ts:42-45) | none | envelope | activate, set_remote_control | OK |
| control/parent (backend.ts:110-114, tools.ts:107) | session.json | none | deliver, tools | bypass R2 |
| delivery (deliver.ts:20-96) | `orch watch` push, `orch watch --list` | ack, blocked posts | push, turn.complete, nudge | OK (push); gates read side files (:71, :73) → R2 |
| tick (deliver.ts:99-103, register.ts:110-111) | lease, turn, session.json | heartbeat, lease | timer 3 s | bypass R8 |
| tools: report/update/send/plan/ask (tools.ts) | `orch decisions`, `orch watch --once` | envelopes, `orch` posts | tool call | OK |
| log-<holder>.txt (backend.ts:117-123) | own log | own log | each line | allowed (diagnostic; not verified as plumbing) |
| peer nudge (stream.ts:217-220) | none | session.send | send | allowed (plumbing) |

**CLI (src/Orchestrator.Cli)**

| Command | Reads | Writes | Verdict |
|---|---|---|---|
| post, send, answer, chat-answer, review, inbox, read, events, decisions | stream | envelopes | OK |
| nudge, init | none | nudges/, folders | allowed (plumbing, setup) |
| watch, events --follow (EventCommands.cs:74-81, 102-120) | stream, cursor | cursor | local: FileSystemWatcher + 60 s sweep → allowed R8. HTTP mode: bypass R5 |
| new (SessionCommands.cs:30 → Messages.cs:56-70 → FileStore.cs:104-109) | none | session.json, enabled, brief post | bypass R3 |
| enable (SessionCommands.cs:40 → Lifecycle.cs:133-157) | session.json | session.json, enabled, session.start post | bypass R3 |
| stop / resume / archive (Lifecycle.cs:21-30, 43-56) | session.json | post + SetPhase + enabled delete | bypass R3 |
| open (LifecycleCommands.cs:47-48; Lifecycle.cs:159-177) | lease, status, turn.json; app records every 1 s | session.json + session.import post | bypass R2, R3 |
| status, tree (SessionCommands.cs:58-125) | session.json, lease, heartbeat, status, stack/ | none | bypass R2 |
| decision start/pr/ready, stack, merge (StackOps.cs, MergeGuard.cs) | stack/ | stack/ + branch.*/pr.* posts | bypass R1/R2/R3 |
| run start/step/stop/show/list (RunCommands.cs → WorkflowRunner.cs:43, 48, 129) | runs/ | runs/ | bypass R1/R2/R3 |
| template, doctor, store migrate/upgrade, supervisor, process | config, store root | config, copy | allowed (config/admin) |

**Tray (src/Orchestrator.Tray)**

| Feature | Reads | Writes | Trigger | Verdict |
|---|---|---|---|---|
| data source (Program.cs:44, :112-128) | in-process FileStore via Web Board/StreamHub; no HTTP, no SSE | envelopes (writer "tray") | StreamHub.Changed | OK for posts; Web actions not verified |
| badge counts (TrayApp.cs:43, 58, 82) | inbox cards (stream) | none | push: FileSystemWatcher on events/ and nudges/ only (StreamWatcher.cs:20) | OK |
| session tree (Board.cs:63-66, Web code used by the tray) | lease, heartbeat, status.json | none | the same push | bypass R2; no push when status or lease changes (stale view) |
| place.json (EdgeTab.cs:451-465, WidgetForm.cs:352-367) | UI file | UI file | drag | allowed (UI state outside the store) |
| animation timers 8-15 ms | none | none | timer | allowed |
| supervisor probe (Program.cs:94) | port | none | start | allowed |
| MockStore (MockStore.cs:87, 91) | none | status.json | mock only | allowed (mock); it repeats P1 |

**Providers (src/Orchestrator.Providers)**

| Feature | Reads | Writes | Verdict |
|---|---|---|---|
| AppRecords (AppRecords.cs:9-21), Read (ClaudeCodeProvider.cs:117-134) | app session files, plugin settings | none | OK (not store data) |
| ProcessTree timer 1 s (ProcessTree.cs:57) | process table | none | allowed (not store data) |
| TurnEnvironment (ClaudeCodeProvider.cs:49-55) | none | env ORCH_HOLDER/STORE/HOME | OK |
| FakeProvider (FakeProvider.cs:65-68) | ORCH_FAKE file | none | OK (test) |

No store writes. The only state outside the stream is in memory.

## 2. Bypass list

**Plugin**

- **P1** register.ts:56-59, :173, :183. R1/R3. The turn start and end write status.json.
  - Fix: post `turn.start` / `turn.end` with turnId. Projection: the session status is the last turn envelope.
  - Test: the turn.start hook posts one envelope and writes no status.json.
- **P2** register.ts:95-96. R2. Activation reads status.json for idle.
  - Fix: read the P1 projection.
- **P3** backend.ts:101-103 (callers lease.ts:345, register.ts:97). R1. The heartbeat is a file write every 3 s.
  - Fix: post `session.heartbeat` at a lower rate, or derive liveness from the lease envelope plus a process check.
  - Decision necessary: at 3 s, the stream gets about 28,800 envelopes for each session each day.
- **P4** lease.ts:327-335 (callers :340, :350, :360; register.ts:100). R1/R3. lease.json is written directly.
  - Fix: post `lease.take` / `lease.release`. Projection: the last lease envelope of the session.
  - Test: a two-writer race resolves from the stream.
- **P5** lease.ts:323-325, :346, :349, :359. R2. The hooks read lease.json and turn.json.
  - Fix: read the P4 and P1 projections.
- **P6** backend.ts:105-107. R1. The mirror/ files repeat session.input/output/tool.
  - Fix: delete the mirror. If the raw row is necessary, put it in the body (`row`).
- **P7** events.ts:282. R1/R3. last-output.json is written.
  - Fix: delete it. Use the projection "last session.output from the session" (FileStore.LastPost exists).
- **P8** events.ts:259. R2. The sender is read from turn.json.
  - Fix: the supervisor `turn.start` envelope carries `from`; read it.
- **P9** backend.ts:93-99; lease.ts:339; register.ts:116, :153, :164. R2/R3/R5. The `enabled` marker is written, and lease.ts:339 polls it every 3 s to detect an archive.
  - Fix: use the session.start / session.archive envelopes. The existing `orch watch` push sees session.archive.
- **P10** register.ts:122-144. R1/R3. /orch-enable writes session.json. This contradicts the one-writer comment at register.ts:41.
  - Fix: post `session.start` / `meta.update`. The registry reactor applies it.
- **P11** backend.ts:110-114, stream.ts:181-185, tools.ts:107, register.ts:103. R2. Many hooks read session.json.
  - Fix: a registry projection from the envelopes.
  - Not verified: whether session.json itself counts as a projection. The brief lists it as a side file.
- **P12** register.ts:110-111, deliver.ts:99-103. R8. The 3 s tick reads side files and writes heartbeat and lease.
  - Fix: the tick only restarts the watch. Delivery starts from the push and turn.complete.

**CLI**

- **C1** SessionCommands.cs:90-125 (FileStore.cs:86-92, :280, :296). R2. status and tree read lease, heartbeat, status and stack/.
  - Fix: use the projections from P1, P4 and C7.
- **C2** LifecycleCommands.cs:47-48. R2. open reads lease, status and turn.json to detect a running turn.
  - Fix: use the projection "open turn".
- **C3** Lifecycle.cs:24, :30, :49-50. R3. stop, resume and archive post, and then also write SetPhase and the `enabled` marker.
  - Fix: post only. A registry reactor projects the phase.
  - Test: stop writes no file under sessions/.
- **C4** Lifecycle.cs:135-155 (orch enable). R3. Registry.Create/Update and SetEnabled.
  - Fix: the session.start post only.
- **C5** Messages.cs:56-69, FileStore.cs:104-109 (orch new). R3. The registry entry and `enabled` are written before the brief.
  - Fix: the brief envelope is the creation fact. The registry is its projection.
- **C6** Lifecycle.cs:175. R3. Import writes Registry.Update and also posts session.import.
  - Fix: post only.
  - The 1 s poll of the app records (Lifecycle.cs:170, :186) is external data, not a store poll. Not verified against R5.
- **C7** StackOps.cs:37, :72, :86, :142 and MergeGuard.cs:69 write stack/. StackOps.cs:16, :158, :165, :187 and MergeGuard.cs:26, :68 read it. R1/R2/R3.
  - Fix: the branch.create, pr.open, pr.ready, pr.close and pr.merge posts exist already. Project the stack from them.
  - Test: rebuild the stack from the stream and compare it with stack/.
- **C8** WorkflowRunner.cs:129 (Put), :43, :48 (Read/List), through RunCommands.cs. R1/R2/R3. The run state is only in runs/.
  - Fix: post `run.start`, `run.step` and `run.stop`. Use a run-state projection.
- **C9** HttpRepositories.cs:78, :236-240. R5. In HTTP mode, `orch watch` and `events --follow` poll every 2 s.
  - Fix: use the SSE endpoint `GET /api/stream/live`.

**Tray**

- **T1** Board.cs:63-66 (Web code that the tray uses) reads lease, heartbeat and status. R2. StreamWatcher.cs:20 watches only events/ and nudges/. Thus a status or lease change sends no push to the tray.
  - Fix: P1 and P4 make these changes envelopes, and the existing watcher then covers them.
  - Test: a turn.end envelope updates the tree without a timer.

**Providers:** no bypass.
