# Store contract

All communication goes through one event stream in the store. A sender posts a body file and an envelope. Each party gets a push for each envelope to its address, then queries and reads what it needs.

## Store root

| Order | Store root |
|---|---|
| 1 | `ORCH_STORE`, when it is set and not empty |
| 2 | The default store root: `%LOCALAPPDATA%\orch\store` (on Windows). With no `LOCALAPPDATA`: `$HOME/.local/share/orch/store` |

Each part uses this one rule: `Home.StoreRoot` (C#: `orch`, the supervisor, the tray), `storeRoot` in `plugin/hooks/backend.ts`, and the default of `install.ps1 -Store`. `StoreBackends.Create` takes the root that `Home.StoreRoot` gives. The test `StoreMoveTests` checks each place.

The default store root is out of the checkout, so a `git clean -fdx` cannot delete it (H1). The old default was `<home>/store` in the checkout. `orch store root` prints the store root. `tray.ps1 -LiveDefault` and `Orchestrator.Tray.exe --live` with no folder show the default store root.

### Migration of the old store

`orch store migrate [--from DIR] [--to DIR] [--dry-run] [--force]` copies the old store to the store root. The default source is `<home>/store`. The default target is the store root.

1. The command refuses the copy in these cases: the source has no `version.json`; the target has data (a file other than `version.json` and `policy.json`); `<target>.migrating` exists; a session of the source has a heartbeat in the last 30 s (`--force` skips only this check).
2. It copies each file to `<target>.migrating`. It skips `*.tmp` files of writes in progress.
3. It compares each file of the source with its copy: the size and the SHA-256 hash. A missing, an extra or a changed file is a difference.
4. With no difference, it renames `<target>.migrating` to the target. Each `orch` command makes a fresh store (only `version.json` and `policy.json`) at its first run. The migration renames such a target to `<target>.fresh-<time>` first; it deletes no file. With a difference, it lists the differences, keeps `<target>.migrating` for examination, and does not make the target.
5. It never changes or deletes the source. It prints the next steps: remove an old `ORCH_STORE`, run `install.ps1` again, start the supervisor, then move the old folder out of the checkout as a backup.

## Layout

```
<store root>/
  version.json                       { version: 3, at }
  policy.json                        the policy in use
  events/<day>/<id>.json             envelopes, append only, one file each
  bodies/<day>/<id>.json             bodies, write once
  subscriptions/<party>/<name>.json  { party, name, cursor, recent, at, filter } of one subscriber
  subscriptions/<party>/<name>.json.lock  the lock of the version check and the write of the cursor
  nudges/<party>                     a nudge: the write time is the signal, the text is an envelope id or empty
  claims/answer/<thread>             the claim of the first answer in a thread: an exclusive create, the text is the sender
  sessions/<id>/session.json         the registry entry of one session (Session registry, below)
  sessions/<id>/                     the lifecycle files of one session (below)
  runs/<id>.json                     the run state of one workflow run (Workflow runs, below)
  runs/<id>.json.lock                the lock of the version check and the write of the run
  supervisor/log.txt                 one line for each turn, relay, default, nudge and blocked delivery
  templates/<kind>/                  an added template, or a replacement of a core template (Types, below)
  templates/modes/<name>/            an added mode, or a replacement of a core mode (Template system, below)
  templates/workflows/<name>/        an added workflow, or a replacement of a core workflow
  templates/[<catalog>/].removed/<name>-<time>-<n>/  the files of a removed or replaced store definition
```

A `<party>` in a file name has `-` in place of `:`, because Windows refuses `:` in a file name: `session:<id>` becomes `session-<id>`.

There is no other communication file. The inbox, the answers, the reviews, the reports and the open decisions are queries of the stream. A reactor can keep a cache if it can make the cache again from the stream (for example `docs-checks/`).

## Repositories (C#)

The C# code reads and writes the store only through `IRepositories` (`src/Orchestrator.Core/Storage/`). `StoreBackends.Create` selects the implementation: an `http` or `https` root gives `HttpRepositories` (the store API of a supervisor, section "Store API"), each other root the local folder. Only the local folder implementation (`src/Orchestrator.Store/Local/`) touches the files above. The test `ArchitectureTests` refuses store IO in other C# files. The plugin hooks write the same files only through `plugin/hooks/backend.ts`.

| Repository | Entity | Id | Files |
|---|---|---|---|
| `Events` | `Doc<Event>` | envelope id | `events/<day>/<id>.json` |
| `Bodies` | `Doc<JsonElement>` | body path | `bodies/<day>/<id>.json` |
| `Policies` | `Doc<Policy>` | `policy` | `policy.json` |
| `StoreVersions` | `Doc<StoreVersion>` | `version` | `version.json` |
| `Claims` | `Doc<string>` (the writer) | `answer/<thread>` | `claims/answer/<thread>` |
| `Cursors` | `Doc<CursorState>` | `<party>/<name>` | `subscriptions/<party>/<name>.json` |
| `Nudges` | `Doc<string>` | `<party>` | `nudges/<party>` |
| `Sessions` | `Doc<string>` (the id) | session id | `sessions/<id>/` |
| `SessionEntries` | `Doc<SessionEntry>` | session id | `sessions/<id>/session.json`. Only `SessionRegistry` reads and writes it (S2) |
| `SessionFile<T>(file)` | `Doc<T>` | session id | `sessions/<id>/<file>` |
| `SessionItems<T>(id, folder)` | `Doc<T>` | item name | `sessions/<id>/<folder>/<name>.json` |
| `Definitions(catalog)` | `Doc<Definition>` (the files of one folder) | definition name | `templates/<kind>/`, `templates/modes/<name>/`, `templates/workflows/<name>/` |
| `WorkflowRuns` | `Doc<WorkflowRun>` | run id | `runs/<id>.json`. Only `WorkflowRunner` writes it (C2) |
| `Log` (`ILogSink`) | none | none | `supervisor/log.txt` |

The version of a `Definition` is the FNV-1a hash of each file path and text, in path order. A change and a remove of a definition move the old folder to `.removed/`.

Rules of each implementation. The contract tests (`RepositoryContractTests`) run them against each data kind, on the local folder and on the store API of a test server:

1. A save of an `Add` with an id that exists fails with `StoreConflictException` (`Exists`). The local folder writes a temp file and moves it with no overwrite. The claims and the envelopes use this rule.
2. Each entity has a version. A save of a change or a remove fails when the stored version is newer (`Stale`). A put of a whole file (lease, meta, status) reads again and writes again: the last writer wins.

### Reads of the envelopes

The envelopes are append only. Two parts keep the reads of each tick small:

| Part | Where | Rule |
|---|---|---|
| Append feed | `IAppendFeed<T>` (`Core/Storage`), on `LocalEventRepository` | `ReadFrom(position)` gives only the envelopes after the position. `EventStream` keeps its position, so a call does not scan the envelopes that it has |
| Day cache | `LocalEventRepository` | A day folder with two newer day folders is closed and never listed again. An open day folder is listed again only when its write time changed, or when its write time is newer than 2 s (`Settle`). The list of day folders (`events/`) has the same cache |
| Index by session | `EventStream.OfSession(id, since)` | The envelopes from a session, to a session, or with the session in the `session` field, in id order |
| Index by sender and kind | `EventStream.LastFrom`, `PostsFrom` | The posts (no forwards) of one sender and one kind. `FileStore.Report` and `FileStore.Decisions` use it |

The day cache needs this rule of the file system: a create, a rename or a delete of a file in a folder changes the write time of the folder. NTFS and the Linux file systems follow it. A writer must create each envelope file in its day folder; a write to a file that exists does not change the folder write time.

### Version rule

The version of a stored file is a 64-bit FNV-1a hash of its UTF-8 text, as a signed 64-bit number. A file that does not exist has version 0. A hash of 0 becomes 1. The C# side and the plugin hooks use this one rule:

| Side | Code | Test |
|---|---|---|
| C# | `Versions.Of` (`src/Orchestrator.Store/Local/LocalFileRepository.cs`) | `VersionRuleTests` |
| Plugin hooks | `versionOf` (`plugin/hooks/backend.ts`) | `backend.test.ts` |

Both tests check the same three known values. The files have no `version` field. A hook save with an expected version (`writeSession`) fails with `StoreConflict` when the file text on disk has another hash.

## Envelope

```json
{
  "id": "1763300000000-a1b2c3d4-cli", "at": "2026-11-16T10:00:00.000Z", "session": "<session id>",
  "stream": "C", "kind": "decision", "from": "session:<id>", "to": "user",
  "subject": "Which color do we use in color.txt?", "body": "bodies/2026-11-16/1763300000000-a1b2c3d4-cli.json",
  "thread": "toolu_01RAQ", "needs": "answer",
  "refs": { "reply": "<id>", "of": "<id>", "via": "session:<root> | chat" }
}
```

| Field | Use |
|---|---|
| `id` | `<ms>-<8 hex>-<writer>`. The ids of one writer always grow. The writer is `supervisor` (the supervisor, or the plugin in a supervisor turn), `cli`, `live` or `tray` (the tray app in live mode) |
| `session` | The session that the envelope is about |
| `stream` | The work stream (C1) of the session of the envelope. Rules below the table |
| `from`, `to` | `user`, `supervisor`, `docs`, `policy` or `session:<full id>`. One receiver for each envelope |
| `subject` | One line: the first sentence (it ends with `.`, `!` or `?` and a space), cut at 80 characters. One rule in `Subjects.Of` (C#) and `subject` (`stream.ts`); the contract tests check both |
| `body` | The path of the body. A forward points to the body of the original |
| `thread` | Groups one conversation. For a question or a plan it is the tool call id; the answer, the review and the notices use the same thread |
| `needs` | `answer`, `review` or `ack`: the receiver must act. Absent: nothing to do |
| `refs.reply` | The envelope that this envelope answers. It closes that envelope |
| `refs.of` | A forward (same body, new receiver) or an ack: the original envelope |
| `refs.via` | The relay that typed the words of the user: `session:<root>` for `mcp__orch__answer`, `chat` for a chat line |
| other `refs` | Traces: `turnStart`, `envelopes`, `row`, `tool`, `output`, `parent`, `envelope` (of `delivery.blocked`) |

Rules of the `stream` field (C4):

1. The writer copies `stream` from the registry entry of the session. The session is `session`, else the session of `from`, else the session of `to`. C#: `EventStream.StreamOf`, through `SessionRegistry.Get` (an `EventStream` with no registry writes no stream). Plugin: `streamOf` (`stream.ts`) reads the entry of its own session.
2. A `stream` in `PostInput` wins over the registry.
3. A forward keeps the `stream` of the original.
4. A session with no stream gives an envelope with no `stream` field. An envelope from before C4 has no `stream` field too. A reader treats both as "no stream". No migration adds the field.
5. The registry sets the stream when it creates the entry and never changes it. So `EventStream` keeps the stream of each entry that it read.

## Primitives

| # | Primitive | C# (`EventStream`) | CLI | HTTP | Plugin (`stream.ts`) |
|---|---|---|---|---|---|
| 0 | Setup | `Open()` | `orch init` | None | Folders on the first write |
| 1 | Post | `Post`, `Forward`, `Ack` | `orch post <kind> --to P`, `orch send`, `orch answer`, `orch review` | `POST /api/stream` | `post`, `ack` |
| 2 | Subscribe | `Subscribe(party, name, filter)`, `StreamWatcher` | `orch watch --to P`, `orch events --follow` | `GET /api/stream/live` (SSE) | A child `orch watch` |
| 3 | Query | `store.Query(StreamQuery)`; indexes `After`, `Thread`, `OfKind`, `FromParty`, `RepliesTo`, `AcksOf`, `ForwardsOf` | `orch events` | `GET /api/stream`, `GET /api/stream/<view>` | `orch watch --list` |
| 4 | Read | `Body`, `Fields`; `Renderer.Text(e, name)` for the receiver | `orch read <id> [--for P] [--render N]` | `GET /api/events/<id>?for=P` | The `text` of each `orch watch` line |
| 5 | Nudge | `Nudge(to, id)` | `orch nudge <party> [id]` | `POST /api/nudge` | `session.send` peer nudge, `session.receive` |

## Query

Each reader asks the stream with one typed record: `StreamQuery` (`Orchestrator.Core/Events/StreamQuery.cs`). All filters must match. A reader never reads a session file for a message.

| Field | Text key | Match |
|---|---|---|
| `Since`, `Until` | `since`, `until` | The id time. A duration (`3h`) or an ISO time |
| `After` | `after` | The ids after this id |
| `Kinds` | `kind` | A comma list |
| `Session` | `session` | The `session` field, `from` or `to`. A prefix of the id is enough |
| `Party` | `party` | `from` or `to`. A prefix is enough |
| `From`, `To` | `from`, `to` | A prefix of the party |
| `Thread` | `thread` | The exact thread |
| `Stream` | `stream` | The `stream` field, exact, no case. An envelope with no stream never matches |
| `Needs` | `needs` | `answer`, `review`, `ack`, or `any` (`needs`, `needs=true`) |
| `Open` | `open` | The envelope waits for an act (the inbox rule below) |
| `Ledger` | `ledger` | The answers by docs or policy, and the reviews |
| `Forwards` | `forwards` | `false`: no forwards |
| `ThreadHeads` | `heads` | Only the newest envelope of each thread |
| `Newest`, `Limit` | `newest`, `limit` | The order and the maximum count |

A flag key is true when it is present with no value, or with `true` or `1`. The key `view` starts from a named query; the other keys add filters.

| Named query (`StreamQueries`) | View | Filters |
|---|---|---|
| `Inbox` | `inbox` | `to = user`, open, the newest of each thread |
| `OpenDecisions(session?)` | `decisions` | question from a session, no forwards, open |
| `Timeline(filter, limit)` | `timeline` | The filter, newest first, 300 maximum |
| `Thread(thread)` | `thread` | One conversation in order |
| `Ledger` | `ledger` | The ledger |

The views of a session are queries too: `Report` and `Update` are the newest post of the session, `Blocked` is the newest `blocked` envelope about the session, and `OpenDecisions` is the named query. The plugin posts `blocked` when a submit drops and when it goes through again (the stop latch); the supervisor (`SaveBlocked`) posts it when the reason of a failed turn changes. The reason is `supervisor turns fail: <error>`, with no count and no time, so it changes only with the error. The count and the next try are in the `failure` envelope. `Blocked.By` is `supervisor` for an envelope from the supervisor, else null (the stop latch). No session file holds the blocked state.

## Post rules

1. Post writes the body first, then the envelope. The envelope is the commit point. A body with no envelope is ignored.
2. Envelopes and bodies never change. A correction is a new post.
3. A message kind must match `templates/<kind>/schema.json`. Its `from` and `to` must be in `meta.json`. A `refs.reply` must name an envelope whose type takes this reply type. An error names the type and each wrong field.
4. Post refuses a second answer in one thread, and gives the first answer in the error text. A forward is not a second answer.
5. A forward is a new envelope with a new `to`, the same body, the same `from` and `thread`, and `refs.of`.
6. Before an answer, the writer adds the claim `answer/<thread>` (file `claims/answer/<thread>`). A second add of the claim fails, so the post is refused with the first answer. So the first answer wins also between processes (the supervisor, `orch`, the UI).
7. The plugin refuses an unknown kind, a message kind with no `meta.json`, missing `required` or `dependentRequired` fields of `schema.json`, and a `from` or `to` that `meta.json` does not allow. The engine checks the full schema before the tool call. The plugin never posts an answer or a review: these go through `orch`, so the reply-type and first-answer checks are in C# only.
8. A question or a plan with no `thread` gets its own envelope id as the thread. So a question from `POST /api/stream` or `orch post` with no thread is open, makes a card and takes an answer, the same as one from `ask_user` (the tool call id).
9. A post of an old question kind (`decision`, `query`) is stored as `question`.
10. The envelope file holds only the fields in the table above. The C# record has computed helpers (`Of`, `Reply`, `Via`, `IsForward`); they are not in the file.

## Types

Each message type has a folder `templates/<type>/` with `schema.json`, `meta.json` (purpose, from, to, needs, replies, card), `view.json`, `example.json`, and optional `render.md` and `render/<name>.md`.

Two roots hold templates:

| Root | Content | Changed by |
|---|---|---|
| `<home>/templates/` | The core templates of the tool, and `_spec/` (`meta.schema.json`, `view.schema.json`, `looks.json`, `mode.schema.json`, `workflow.schema.json`) | A commit |
| `<store>/templates/` | Added templates. A folder with a core name replaces that core template | `orch template add <folder> [--replace]`, `orch template remove <kind>` |

The code reads templates only through `ITemplateStore` (`List`, `Get`, `Add`, `Remove`). `TemplateStore` is the one implementation: a thin slice on two `Definitions(Catalog.Templates)` repositories (Template system, below). `TemplateCheck` checks a template against `_spec/`: the name (`^[a-z][a-z0-9-]{1,31}$`, not `list`, `check`, `add`, `remove`, `modes` or `workflows`), the meta and the view against their schemas, each view field in the schema, each look in `looks.json` and in an allowed place, and the example against the schema. `add` refuses a template that does not pass. A store template that does not load is skipped, and `orch template` and `orch doctor` show the error. A running supervisor reads the templates at start: restart it after a change.

An added kind needs no code change. The stream checks its schema and meta, the UI shows it with its `view.json`, and a session gets `render/session.md` if it exists.

| Type | From | To | Needs | Replies | Render for a session |
|---|---|---|---|---|---|
| `brief` | user, session, supervisor | session | | report | `render/session.md` |
| `message` | user, session, supervisor | session, user | | message | `render/session.md`: "Message from <sender>:"; no prefix from the user |
| `question` | session | user; forward to the parent | answer | answer | `render/parent.md`: "Question from child" |
| `plan` | session | user; forward to the parent | review | review | `render/parent.md`: "Plan from child" |
| `report` | session | user; forward to the parent | ack (done, blocked) | ack, message | `render/parent.md`: "Report from child" |
| `update` | session | user | | | Not delivered |
| `answer` | user, docs, policy | session; forward to the user | review (docs, policy) | review | `render/session.md` + `render/note-*.md`; `render/tool.md` for `ask_user` |
| `review` | user | session | | | `render/<verdict>.md`, `render/plan.md` |
| `notice` | supervisor | session | | | `render/<kind>.md`: `question-reminder`, `question-closed`, `archive-self`, `restart`, and the lifecycle notices (below) |
| `ack` | session, user, supervisor | the sender | | | Not delivered |
| `failure` | supervisor | user | | ack, message | Not delivered |

A `failure` is a failure that the supervisor saw (D, problem 4). Fields: `text`, `session`, `reason` (`failed-turns` or `no-ack`), `state` (`open` or `recovered`), `error`, `tries`, `first`, `last`, `next`, `envelopes`. Each session and reason has one thread, `failure-<session>-<reason>` (`Failures.Thread`):

| Moment | Post |
|---|---|
| `MaxFailures` failed supervisor turns, and each failed try after it | `failure` with `state: open`, the tries, the error, the next try and the waiting envelopes |
| A live session does not take an envelope after the nudge | `delivery.blocked` (a trace) for the envelope, then `failure` `no-ack` with all stuck envelopes |
| The same facts again | Nothing |
| A good turn, or no stuck envelope | `failure` with `state: recovered` |

The envelope has no `needs`: it knows nothing about cards. The inbox view decides (User inbox, below).

Other kinds have no template: traces (`session.input`, `session.output`, `session.tool`, `turn.start`, `turn.end`, `turn.fail`), lifecycle names of stream B (`session.start`, `session.stop`, `session.resume`, `session.archive`, `session.import`, `session.end`, `session.state`, `state.conflict`, `meta.update`, `lease.take`, `lease.release`, `blocked`, `delivery.blocked`). `delivery.blocked` is a trace with no `needs`: to the user, and a copy to a sender session and git (`branch.*`, `pr.*`, `merge.block`).

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
| `open-in-app` | Session | The only input of the first turn of a new session when the supervisor imports sessions (`ORCH_IMPORT`). The turn makes the transcript, the import follows, and the brief waits for the live app session |
| `remote-control-self` | Session | After the import into the app, unless the setting `behavior.visibility` is `private` (F4). Field `remoteControlTool` |
| `remote-control-child` | Parent | `remoteControlSeconds` after `remote-control-self`, and `engine.remoteControl` of the entry is not set. Fields `appId`, `remoteControlTool` |
| `archive-child` | Parent | F8, path 2: the app still shows a session that is archived in the store. At once when the session is not live (it cannot archive itself), else at the deadline of the request. The parent turns Remote Control off, then archives it in the app (probes 2a to 2c). Fields `appId`, `remoteControlTool`, `archiveTool` |
| `state-inferred` | Parent | The fallback agent inferred the state. Fields `state`, `text` |

The provider gives the app tool names: `remoteControlTool` and `archiveTool` (`IProvider.AppTools`). The supervisor adds them to `remote-control-self`, `remote-control-child`, `archive-child` and `archive-self`. No template names an app tool.

A root session has no parent: each notice to the parent goes to the user as a `message` from `supervisor` with `needs: ack`. The same applies to a child whose parent is not enabled. Its text is the user render of the notice, `render/user-<kind>.md`: it names a "session", not a "child", and gives the acts of the user. Each notice kind to the parent has a user render (test `Each_notice_to_the_parent_has_a_render_for_the_user`). A user render is not a notice kind. All lifecycle messages of one session to the user share the thread `notice-<session>`. A new one gets the older open ones an ack from the supervisor (`by: superseded`, `state: read`), so the inbox shows one card for each session: its newest state.

## Template system

The template system has three catalogs. Each definition is one folder with the marker file of its catalog. A folder with no marker file, or a name that starts with `_` or `.`, is not a definition.

| Catalog | Folder | Marker | Spec | C# slice | Read by |
|---|---|---|---|---|---|
| `templates` | `templates/<kind>/` | `schema.json` | `meta.schema.json`, `view.schema.json`, `looks.json` | `TemplateStore` (`ITemplateStore`), `TemplateSet` | The stream, the UI, the renders, the hooks |
| `modes` | `templates/modes/<name>/` | `mode.json` | `mode.schema.json` | `DefinitionCatalog<ModeDefinition>` (`Home.Modes`) | S4 (the readers of the four axes) |
| `workflows` | `templates/workflows/<name>/` | `workflow.json` | `workflow.schema.json` | `DefinitionCatalog<WorkflowDefinition>` (`Home.Workflows`) | C2 (the runner) |

```
templates/
├─ _spec/                    the spec files: one schema for each marker file, and the look catalog
├─ <kind>/                   one message template: schema.json, meta.json, view.json, example.json, render
├─ modes/<name>/             one session mode: mode.json, optional render/*.md
│    auto, chat, ask, approve    the four presets of S1
└─ workflows/<name>/         one workflow: workflow.json, optional render/*.md
     planning                    the first workflow of C1
```

Each catalog has two layers, `LayeredDefinitions`: the core definitions of `<home>/templates/` and the store definitions of `<store>/templates/`. A store definition with a core name replaces the core definition. A core definition cannot be removed. The plugin hook `readDefinition` (`backend.ts`) reads the same two roots with the same marker and name rules; `readTemplate` is the `templates` catalog.

A mode (`mode.json`):

| Field | Values |
|---|---|
| `name`, `version`, `describe` | The folder name, 1 or more, one sentence |
| `interaction` | `task`, `stepwise`, `conversation` |
| `autonomy` | `auto`, `ask`, `approve` |
| `route` | `parent`, `user` |
| `visibility` | `public`, `private` |

A workflow (`workflow.json`): `name`, `version`, `purpose`, `entry` (the id of the entry step), `done` (the definition of done) and `steps`. Each step:

| Field | Content |
|---|---|
| `id`, `title`, `instructions` | Required |
| `executor` | `agent` (one turn of a session), `ask` (a human answers), `tool` (a deterministic call) |
| `session` | Agent steps only: `owner` (default), `new`, `{ "session": id }` or `{ "type": name }` |
| `tool` | Tool steps only, and required there: the name of the deterministic tool (`IWorkflowTool`) |
| `when`, `exit` | The entry and exit conditions, as text |
| `input`, `output`, `templates` | Lists. Each template is a message kind that must exist |
| `approval` | `{ by, strategy }`, strategy `required`, `skippable` or `ignored` |
| `messages` | `[{ kind, to, when }]` |
| `next` | `[{ step, when }]`. No `when`: always valid |
| `done` | `true`: this step can end the work |

`WorkflowDefinition.Check` refuses a repeated step id, an unknown entry step, a `next` to an unknown step, a session on a step that is not an agent step, a tool step with no tool, a tool on a step that is not a tool step, an unknown message kind, a workflow with no `done` step, and a step that the entry step does not reach. `orch template check` checks each template, mode and workflow. `orch template modes` and `orch template workflows` list them.

## Workflow runs

The slice `WorkflowRunner` (`Orchestrator.Core/Workflows/`) is the only writer of `runs/<id>.json` (C2). The id is `<workflow>-<8 hex>`. `orch run` reads and writes runs through `WorkflowHost.Runner`.

| Field | Content |
|---|---|
| `id`, `workflow`, `version`, `owner` | The run, the workflow name and version at the start, the owner session |
| `status` | `running`, `waiting`, `stopped` or `done` |
| `step` | The id of the current step |
| `wait` | `{ kind, text, options, session, by, thread }`. Kind: `agent`, `ask`, `tool`, `exit`, `approval`, `pick` or `stuck` |
| `input`, `outputs` | The run input (for example `goal`), and the outputs of all steps by name |
| `history` | One entry for each step visit: `step`, `executor`, `started`, `ended`, `session`, `thread`, `holds`, `output`, `answer`, `approval`, `next` |
| `reason`, `created`, `updated` | Why the run stopped or ended; stamps `{ at, by }` |

How a run moves:

```
orch run start <workflow> --owner S   owner role → orchestrator, workflow → name; the entry step starts
  executor.Start(step)
    tool   IWorkflowTool.Run → a result at once, or wait "tool"
    ask    a question (kind question, thread run-<hex>) to approval.by or the user → wait "ask"
    agent  the step prompt to the session of the step → wait "agent"
orch run step <run> [--holds "c1; c2"] [--pick S] [--answer T] [--verdict V] [--skip-approval] [--done]
  1. the result: the input of the command, else executor.Poll (the answer of the question thread)
  2. exit condition not in the holds → wait "exit"
  3. approval: ignored → go on; else the verdict, a skip (skippable only), or a question to approval.by → wait "approval"; refuse → stopped
  4. a done step and (the definition of done is in the holds, or --done) → done, a message to the owner
  5. valid next steps: the next condition and the entry condition of the target are in the holds (no text: true)
       one → go · many → the pick, else wait "pick" · none → a question to the user, wait "stuck"
  6. more than 200 step visits → stopped
```

The conditions are text. A condition holds when the holds of the visit have the same text, with no case and no end period. An approval verdict adds the fact `the verdict is <verdict>`. The answer of an ask step meets its exit condition, and for a step with an approval it is also the verdict (its first word).

An agent step names its session: `owner`, `new` (a child of the owner with role worker), `{ "session": id }`, or `{ "type": name }` (an active session of that type in the work tree of the owner, else a new child of that type). A step session that is not the owner gets the role worker, unless it owns a run. New sessions start through `Messages.NewSession`: the registry entry and the brief. The supervisor then runs the turn through `IProvider`.

## Subscriptions and delivery

| Subscription | Party | Filter | Who moves the cursor |
|---|---|---|---|
| `subscriptions/session-<id>/deliver.json` | The session | `to = self`, a deliverable kind | The supervisor and `orch watch`, after each ack |
| `subscriptions/supervisor/relay.json` | The supervisor | question, plan, and a report with `needs` from a session | The supervisor, after the forward |

- A subscriber waits for a push of `IRepositories.Watch()` (local folder: `StreamWatcher`, a file watcher on `events/` and `nudges/`). It also queries from its cursor after `delivery.sweepSeconds` with no push, because a file watcher can drop events.
- `Next()` gives the matching envelopes after the cursor that the subscriber did not mark done. Envelopes of other writers can arrive a little late, so the scan starts 120 s before the cursor, and the file keeps the done ids of these 120 s in `recent`. Limit: an envelope that appears more than 120 s after its id time can be missed by a subscription.
- The delivery cursor moves only over acknowledged envelopes before the first open one. It never passes an envelope that waits for its ack.
- `Done` reads the cursor, merges the done ids and saves it. The save checks the version under the lock `<name>.json.lock`. If another writer saved first, the save fails and `Done` reads and merges again. So two writers of one cursor (the supervisor and `orch watch`) lose no done id.
- The deliverable kinds are brief, message, question, plan, answer, review, report and notice. A report is deliverable only with `needs` or as a forward.
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
| 2 | Still no ack | `delivery.blocked` (a trace) to the user and to a sender session, and one `failure` `no-ack` for the session | `peerSeconds` (60) more |
| 3 | Not live | The turn runner delivers. After `MaxFailures` failed turns: one `failure` `failed-turns` for the session | |

The watchdog does not count the time while the live session runs a turn. A session sends a peer nudge (`$.session.send` with `[orch nudge]`) after a message to another session; the receiver consumes it in `session.receive`. The supervisor sends no peer nudge.

## Reactors

| Reactor | Party | Reaction | Posts |
|---|---|---|---|
| Session deliverer | Plugin, holder `live` | Submits a turn | ack |
| Turn runner | Supervisor | `claude -p` with the renders | `turn.*`, ack |
| Ask wait | Plugin, `ask_user` with a wait | Returns the tool result | ack |
| Question relay | Supervisor | Forwards each question and plan of a child to its parent at once | forward |
| Report relay | Supervisor (subscription `reports-<id>` for each session) | At the end of a turn, forwards the newest done or blocked report of a child to its parent. A turn is open while the supervisor turn runs, or while `status.json` says running and the lease is `live` (an old heartbeat does not end it). An older report of the same turn gets an ack `{ state: read, by: superseded }` from the supervisor (F5). An ack of the user does not stop the forward | forward, ack |
| Lifecycle | Supervisor | One step of the state machine for each session at each tick (below) | `session.state`, `state.conflict`, `lease.release`, notices |
| Meta writer | Supervisor (subscription `meta`) | Applies each `meta.update { session, field, value }` from a plugin: `appId` of the sender itself, `remoteControl` of the sender or of its child. Other updates are refused and logged. The registry writes the field in `engine` | `session.json` |
| Question follow | Supervisor | A closed notice when the question of a child closes; reminders while it waits with no default (10 min, then each hour) | notice |
| Docs check, default | Supervisor (`docs`, `policy`) | An answer to the session, and a forward to the user with `needs: review` | answer, forward |
| User inbox | UI, `orch inbox`, `GET /api/inbox` | Shows a card | answer, review, ack, message |
| Watchdog | Supervisor | Nudge, then `delivery.blocked` and `failure` | `delivery.blocked`, `failure` |

### User inbox

```
open cards = envelopes to = user with needs (or a question with no answer)
             minus envelopes with a reply (refs.reply) or an ack (refs.of)
             minus a report with needs ack whose forward has an ack (the parent got it)
             plus the newest failure of each failure thread, while its state is open and it has no ack
             the newest envelope of each thread
```

| Card | Accept posts | Refuse posts |
|---|---|---|
| question | answer = `defaultOption` (or the text); with no default, the text is required | answer = the text |
| answer by docs or policy | review approve | review refuse with the text |
| report done or blocked, `failure` | ack (`state: read`): "Seen" | message with the text to the session, then ack |

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
| `session.json` | `SessionRegistry` (in the supervisor and in `orch`), and `/orch-enable` (create, or a new title of its own entry, with the version of the read). A plugin never changes another field: it posts `meta.update`, and the registry writes the field | The registry entry: the section "Session registry" below. The supervisor runs no turn of a stopped or archived session (`lifecycle.phase`), and ends its running turn. The plugin delivers no envelope while the phase is not `active` |
| `meta.json`, `control.json` | Store version 1 and 2 only | `orch store upgrade` reads them, and keeps them as a backup. No other code reads them |
| `state.json` | Supervisor | The lifecycle state: `{ state, since, requested, inferred, guard, fault, waitReason, reminded, remoteControlAsked, history }` (below) |
| `state-check.json` | Supervisor | `{ state, reason, at, error }`: the last answer of the fallback agent `orch:state-checker` |
| `forgotten.json` | Supervisor | `{ report, nudged, notified }`: one forgotten episode. `report` is `<last report id>|<last input id>` (an input from another party than the supervisor). A new report or a new input starts a new episode: one nudge, then one notice to the parent. The file is deleted when the session goes from forgotten to done, stopped or archived |
| `docs-checks/<id>.json` | Supervisor | `{ answered, answer, source, at }`: the cache of the docs check of one decision |
| `turn.json` | Supervisor | `{ event, from, at, ended }`: the `turn.start` of the last supervisor turn, its senders, and the end time. The plugin uses `from` as the sender of `session.input` |
| `last-output.json` | Plugin | `{ event, at }`: the last `session.output`. `turn.end` links it in `refs.output` |
| `stack/<decision>.json` | `orch decision`, `orch stack` | The versions of one decision on the stack (below) |

## Session registry

Store version 3 keeps the data of each session in one registry entry, `sessions/<id>/session.json`. The slice `SessionRegistry` (`Orchestrator.Core/Sessions/`) is the only writer, on the repository `SessionEntries`. `orch`, the supervisor, the API and the UI read and write sessions through `FileStore.Registry`. The test `ArchitectureTests` refuses other uses. The run state (lease, heartbeat, status, turn, `state.json`) stays in its own files.

```json
{
  "id": "6f1c2a90-…", "code": "C2",
  "engine": { "kind": "Claude Code", "sessionId": "6f1c2a90-…", "appId": "local_6f1c2a90-…", "imported": true, "remoteControl": true },
  "parent": "ca72efff-…", "title": "Session registry", "owner": "user", "tags": ["orch"], "stream": "S",
  "role": "worker", "type": { "agent": "worker", "skills": ["orch:orch-report"] }, "workflow": "planning",
  "work": { "cwd": "…", "repo": "…", "branch": "orch/session-6f1c2a90", "worktree": true, "where": null },
  "settings": { "behavior": { "mode": "auto", "visibility": "public" }, "permissions": { "mode": "auto", "allowDefaults": false } },
  "lifecycle": { "phase": "active", "at": "…", "by": "user", "reason": null, "started": true },
  "created": { "at": "…", "by": "session:ca72efff-…" }, "updated": { "at": "…", "by": "supervisor" }
}
```

| Field | Content | Written by |
|---|---|---|
| `id` | The registry id. Today it is the engine session id | Create |
| `code` | The own code, unique among the siblings. The registry gives `O<n>` to a root and `C<n>` to a child when the creator gives none | Create; the supervisor tick for an entry of `/orch-enable` |
| `engine` | References to the engine only: `kind` (`IProvider.Name`), `sessionId`, `appId`. Two records of our own actions: `imported` (the import is confirmed) and `remoteControl` (a session reported a change) | Create, the import, `meta.update` |
| `parent` | The parent id. Null for a root. The children are a query (`SessionRegistry.Children`), not a field | Create |
| `title`, `owner`, `tags`, `stream` | Labels. `owner` and `stream` are copies of the parent when the creator gives none | Create, `orch enable` |
| `role` | `orchestrator`, `worker` or `free` (C1). At create: `orchestrator` for the orchestrator agent, `worker` for a child, else `free`. The workflow runner (C2) changes it | Create |
| `type` | The custom agent: `agent` and `skills`. Only the provider reads it, to start the session. With no `type.agent`, a turn uses the default agent of the role: `orchestrator` or `worker` (C2). A `free` session has no agent | Create |
| `workflow` | The workflow that the session runs or works in | Create |
| `work` | `cwd`, and the git worktree: `worktree`, `repo`, `branch`. `where`: `cloud` for a cloud session (G2), else null. A cloud session has no worktree, and `engine.appId` is its cloud session id | Create, the worktree step, the first cloud turn (`engine.appId`) |
| `settings` | The settings in tiers (below) | Create, `SessionRegistry.Configure` |
| `lifecycle` | `phase` (`active`, `stopped`, `archived`) with `at`, `by` and `reason`; `started` (the engine session exists) | `orch stop`, `resume`, `archive`, the API, the supervisor |
| `created`, `updated` | A time and a party | Each write |

The full code is the codes from the root down, with a space between them: `O1 C2 C1`. The app title is `[<full code>] <title>` (`SessionRegistry.AppTitle`). The registry does not store the full code, so a changed code of a parent cannot make it old. A code has no space.

### Settings

| Tier | Key | Values | Root default |
|---|---|---|---|
| `behavior` | `mode` | A mode of the mode catalog (`templates/modes/`): `auto`, `chat`, `ask`, `approve`, or an added mode | `auto` |
| `behavior` | `visibility` and the other axes of a mode | An axis value that changes one value of the mode | `public` |
| `permissions` | `mode` | `auto`, `bypass` | `auto` |
| `permissions` | `allowDefaults` | `true`: `orch merge` can carry unapproved defaults | `false` |

1. At create, each value that the creator sets comes first. Each other value is a copy of the value of the direct parent at that time. A root copies the defaults (`SettingTiers`).
2. Only the user and the parent change a setting of a session (`SessionRegistry.Configure`). The change does not go to the children.
3. A read (`SessionRegistry.Resolve`) takes the own value, then the value of the parent chain, then the default. So a key that a later version adds has a value in an old entry.
4. A new behavior mode is a folder in `templates/modes/`. A new tier, key or permissions mode is one line in `SettingTiers`.
5. Today only two readers use the settings: `visibility` `private` keeps Remote Control off (the old `background`), and `orch merge` reads `allowDefaults` on the session and its parents. S3 and S4 add the other readers.

### Upgrade from version 1 or 2

`orch store upgrade [--dry-run] [--force]` makes a store of version 1 or 2 a store of version 3. It runs only through the repositories. To upgrade the old store `<home>/store`, copy it first with `orch store migrate`, then run the upgrade on the copy.

1. It refuses while a session has a heartbeat in the last 30 s (`--force` skips this check).
2. For each session folder with `meta.json` or `control.json`, it makes the entry: the fields of `meta.json`, the phase of `control.json`, `background` as `behavior.visibility`, `allowDefaults` as `permissions.allowDefaults`. Parents come first, so each child copies the settings of its parent.
3. Codes: `O<n>` for each root and `C<n>` under each parent, oldest first.
4. A meta with no `createdBy` gets `lifecycle.started: true`: version 2 made no new engine session for it.
5. An entry that exists stays. It writes `version.json` last, so a stopped upgrade can run again.
6. It deletes no file: `meta.json` and `control.json` stay as a backup.
7. Version 1 only: the history mark. The `meta.json` and `control.json` of version 1 have the same fields, so steps 2 to 6 are the same. The envelopes of version 1 have no `thread`, no `needs` and no acks, and version 1 had no subscriptions. So each old envelope to a session looks undelivered, and each old `decision` of a child looks not relayed. The upgrade writes a cursor at the newest envelope, with the ids of the last 120 s in `recent`, for each subscription whose filter can match an old envelope: `session-<id>/deliver` and `supervisor/reports-<id>` of each session folder, `supervisor/relay` and `supervisor/meta`. A cursor that is newer stays.
8. Version 1 only: the envelopes do not change. They stay readable as history (`orch events`, the timeline, the report of a session). A question of version 1 keeps its decision id in `refs.decision`, not in `thread`, so it is never open, never makes a card and never gets a default. The old kinds (`question.forward`, `report.forward`, `question.remind`, `question.close`) and the old parties (`plugin`, `composer`, `peer`) are only traces. The communication files of version 1 (`inbox/`, `answers/`, `decisions/`, `delivered/`, `reviews/`, `reports/`, `report.json`, `update.json`, `blocked.json`, `cursor.txt`, `forwarded*`) stay as a backup. No code reads them.

## Lifecycle state machine

The code is in `Orchestrator.Core/Lifecycle` (no IO) and `Supervisor.Lifecycle.cs`. The design is `docs/lifecycle-design.html`. At each tick, the supervisor reads a snapshot of each session and runs one step. It writes `state.json`, and posts `session.state` when the state, the request or the escalation changes.

| State | Detect check (no `state.json`) |
|---|---|
| `archived` | `lifecycle.phase` archived, or no `enabled` |
| `stopped` | `lifecycle.phase` stopped |
| `stale` | Lease `live`, heartbeat and lease older than `staleSeconds` |
| `not-responding` | One fault: failed turns, stop latch, input not taken, hung turn, hung process |
| `start` | `lifecycle.started` is not true, and no turn runs |
| `running` | A supervisor turn, or `status.json` running with a fresh heartbeat |
| `done` | The last report is done, and no input arrived after it |
| `forgotten` | Idle for `forgottenMinutes`, last report not done or blocked, no wait reason, no input after the turn |
| `idle` | No turn |

The checks run in this order, and the first yes wins. With a `state.json`, only the transitions that leave the current state are checked. The four global transitions come first: archive, stop, stale, fault, each only from a state with a higher rank. Then the local transitions, by priority. Two local guards with the same priority are a conflict: the state stays, `state.conflict` is posted, and the fallback agent runs. A `state.json` that is not valid also goes to the fallback agent. An inferred state has `inferred: true` and starts no action.

`state.json`:

```json
{ "state": "idle", "since": "<time>", "guard": "turn-end", "waitReason": "none",
  "requested": { "to": "archived", "transition": "archive", "since": "<time>", "deadline": "<time>", "waitsFor": ["no supervisor turn", "enabled gone", "app archived"], "escalations": 0 },
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

## Question, answer and review

A question is the envelope `question` from a session, with `thread` = the tool call id and the question template as body. The thread id is the decision id. The target (`to`) gives the direction. A question is open while its thread has no answer that is not a forward.

| Question fields | Rule (in `templates/question/schema.json`) |
|---|---|
| `question` | Required |
| `risk` | Required when `defaultOption` is present (`dependentRequired`) |
| `context`, `snippet`, `options`, `recommendation`, `defaultOption`, `waitSeconds` | Optional |

| Form | Fields | Policy |
|---|---|---|
| Choice (`Decision.Kind` = `ask_user`) | A `risk`, often `options` and a `defaultOption` | Docs check and default by the level of `risk` |
| Facts (`Decision.Kind` = `query`) | No `risk` and no `defaultOption` | None: only a party answers. The risk reads as `high` |

Old stores have the kinds `decision` and `query`. The stream reads them as `question` (`EventKinds.Current`), so these kinds need no store version change.

| Read | Query |
|---|---|
| `Decision(thread)` | The first question in the thread that is not a forward |
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
| `GET /api/stream` | Query: the text keys of `StreamQuery` (see Query) | The matching envelopes |
| `GET /api/stream/inbox`, `/decisions`, `/timeline`, `/ledger` | Query: more text keys | The envelopes of the named query |
| `GET /api/stream/threads/<thread>` | Query: more text keys | The envelopes of one thread |
| `GET /api/stream/live` | Query: `after` and the text keys | Server-sent events: one `data:` line with the envelope for each new envelope that matches |
| `GET /api/events` | Query: the text keys | The same as `GET /api/stream` |
| `GET /api/events/<id>` | Query: `for`, `render` | `{ envelope, body, text }`. `text` is the render for the receiver |
| `POST /api/nudge` | `{ to, id? }` | `{ nudged }` |
| `GET /api/inbox` | | The open cards: `{ id, kind, needs, session, title, envelope, fields, defaultAt }`. For a question, `kind` is `question`. `defaultAt` is the ISO time when the policy answers an open decision with its `defaultOption` (the time of the decision + `timeoutSeconds` of its level). It is `null` for a query, a decision with no default, a level with no timeout, and each card that is not a decision |
| `POST /api/inbox/<id>/accept` | `{ text? }` | The posted envelope |
| `POST /api/inbox/<id>/refuse` | `{ text }` | The posted envelope |
| `GET /api/sessions` | | Each enabled session: title, parent, holder, live, running, status, report, blocked, open decisions, and from `state.json`: `state`, `waitReason`, `requested`, `fault`, `inferred` |
| `GET /api/decisions` | | All open decisions |
| `POST /api/sessions` | `{ title, cwd, brief, parent?, background?, where? }` | `{ id }`. A brief to the new session. `where: cloud` starts a cloud session |
| `POST /api/sessions/<id>/send` | `{ text }` | `{ event }`. A message from the user |
| `POST /api/decisions/<session>/<id>` | `{ answer }` | `{ answered }`. An answer from the user |
| `POST /api/decisions/<session>/<id>/review` | `{ verdict: approve \| change \| refuse, text }` | `{ event }`. A review to the session |
| `GET /api/ledger` | | The answers by docs or policy, and each review |
| `POST /api/sessions/<id>/stop`, `/resume`, `/archive?force=true` | | Lifecycle (stream B) |

### Token check

| Caller | Rule |
|---|---|
| Local: address `127.0.0.1` or `::1`, no proxy header | No token |
| Remote, `ORCH_TOKEN` set | Header `X-Orch-Token`, `Authorization: Bearer`, or the sign-in cookie. Else 401 |
| Remote, no `ORCH_TOKEN` | Pages and `/api/*` open; `/api/store` gives 403 |

Proxy headers: `X-Forwarded-For`, `Forwarded`, `X-Real-IP`, `Cf-Connecting-Ip`, `Cf-Access-Jwt-Assertion`. A tunnel connects from `127.0.0.1`, so these headers make a request remote.

## Store API (H5, `/api/store`)

The store API gives each repository of `IRepositories` to a remote caller: `orch` in HTTP mode (`HttpRepositories`). `StoreService` applies each call to the local repositories of the supervisor. A document goes as its file text, so its version is the same on both sides (Version rule).

```mermaid
flowchart LR
    W[Cloud worker: orch] -->|HTTPS + token| A[/api/store]
    A --> S[StoreService]
    S --> R[Local repositories]
    R --> F[(Store folder)]
```

| Call | Body or query | Result |
|---|---|---|
| `GET /api/store` | | `{ location, exists, version }` |
| `POST /api/store/prepare` | | Makes the stream folders |
| `POST /api/store/log` | `{ line }` | 204. One line in the supervisor log |
| `GET /api/store/<kind>/get` | `id` and the scope keys | `{ id, version, text }`, or 404 |
| `GET /api/store/<kind>/list` | The scope keys | All documents of the kind |
| `POST /api/store/<kind>/save` | `{ adds, changes, removes }`: `{ id, version, text }` each | `{ versions }`. 409 `{ conflict, id, message }` on a store rule |
| `GET /api/store/events/feed` | `from` | `{ epoch, position, docs }`: the envelopes after `from`. A new supervisor process has a new `epoch` |

| Kind | Scope keys |
|---|---|
| `events`, `bodies`, `policies`, `versions`, `claims`, `cursors`, `nudges`, `sessions` | None |
| `session-file` | `file`, for example `lease.json` |
| `session-items` | `session`, `folder` |
| `definitions` | `catalog`: `templates`, `modes` or `workflows` |

- An id is one or more parts with `/` between them. The API refuses an empty part, `.`, `..`, `\`, `:` and control characters (400). A scope key is one part.
- A save checks the version of each change and remove before the first write. A refused save writes nothing.
- The HTTP mode has no push: `Watch()` polls every 2 s.

## Questions to the user

The user answers in free text, never with a click. The `options` of a question are suggestions only.

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
4. The supervisor and each `orch` command (except `doctor`, `template`, `process`, `supervisor` and `store`) refuse a store with another version. The plugin stays inactive in a store whose `version.json` is not version 3. A store of version 1 or 2 needs `orch store upgrade` (the section "Session registry").

## Recovery

The supervisor can stop at any moment, for example in a crash. At each tick, it examines the `turn.json` of each session that has no running turn:

| Case | Action |
|---|---|
| `ended` is set | None |
| No `ended`, and the lease holder is `supervisor` with a heartbeat less than 15 s old | Wait: the old `claude` process still runs |
| No `ended`, and a `session.input` has `refs.turnStart` of this turn | The prompt arrived: an ack for each envelope in `refs.envelopes` of `turn.start`, and `turn.end` with `recovered: true` |
| No `ended`, and no such envelope | The prompt did not arrive: `turn.fail` with `recovered: true`. The envelopes have no ack, so they go in the next turn |
