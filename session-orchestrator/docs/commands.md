# Commands of the orchestrator

This file lists each command, tool, skill, agent and hook that an orchestrator session uses. It gives the session types that get each one, and the rules for the session lifecycle. `orch --help` gives the full options of each command.

## Install

| Part | Source | Install |
|---|---|---|
| `orch` plugin | `plugins/orch` in the `mgcohen` marketplace (`MgCohen/claude-plugins`) | Account-synced: each signed-in session gets it |
| `orch` CLI | `src/Orchestrator.Cli`, a .NET global tool | `.\install.ps1`, or `dotnet tool update -g Orchestrator.Cli` |
| Supervisor | `src/Orchestrator.Web` | `.\install.ps1`, then `orch supervisor start` |

- The plugin source is `plugin/` in this folder. `tools/publish-plugin.ps1` copies it to the marketplace repository.
- The plugin finds `orch` on the PATH. It finds the store through `ORCH_STORE`, else `%LOCALAPPDATA%\orch\store`. It finds the templates through `ORCH_HOME`, else `orch home`.
- The CLI carries a copy of `templates/` and `config/` next to its program. `orch home` prints the folder that it uses.
- For plugin development, start a session with `claude --plugin-dir <path of plugin/>`. A test supervisor with `ORCH_PLUGIN_DIR` does the same for its turns.

## Session types

| Type | What it is | Plugin tools | App tools |
|---|---|---|---|
| App | A turn in the desktop app | Yes, when enabled | Yes |
| Remote Control | A turn that came from the phone or claude.ai | Yes, when enabled | Yes. `start_session` is refused |
| Supervisor | A `claude -p` turn of the supervisor | Yes, when enabled | No |
| Old | A session that started before the plugin loaded | No. A hot reload gives the skills only | Yes |

"When enabled": the session has `<store>/sessions/<id>/enabled`, and no `ORCH_URL` is set. An old session uses only the CLI: `orch new --parent <its id>` starts a child through the supervisor.

## The orch CLI

All session types get the CLI, if the session has a shell and `orch` is on the PATH.

| Group | Commands | Reference |
|---|---|---|
| Sessions | `new`, `enable`, `join`, `whoami`, `send`, `tree`, `status`, `stop`, `resume`, `archive`, `open` | `store.md`, "Session lifecycle" in `know-how.md` |
| Workflow runs | `run start`, `run show`, `run list`, `run step`, `run stop` | `store.md` "Workflow runs" |
| Questions | `questions` (`decisions`), `answer`, `chat-answer`, `inbox`, `review` | `store.md` "Question, answer and review" |
| Stack | `decision start/pr/ready`, `stack rebase/close`, `merge` | `store.md` "Stack", skill `orch-stack` |
| Stream | `init`, `post`, `events`, `read`, `watch`, `nudge` | `store.md` "Envelope" |
| Templates | `template [list, <kind>, check, modes, workflows, add, remove]` | `store.md` "Template system" |
| Setup | `doctor`, `home`, `store [root, migrate, upgrade]`, `supervisor start/stop/status`, `process` | `know-how.md` "The orch tool" |

Options that `orch --help` does not show: `orch new --agent`, `--created-by`, `--goal-file`, `--app-id`. The plugin and the supervisor use them.

## The plugin

| Item | Kind | Session types |
|---|---|---|
| `mcp__orch__ask_user`, `plan`, `report`, `update`, `send`, `answer`, `questions`, `spawn_child` | Tool | App, Remote Control, Supervisor |
| `/orch-enable [title]` | Slash command | App, Remote Control |
| `orch:orch-ask`, `orch-brief`, `orch-messages`, `orch-reply`, `orch-report`, `orch-stack` | Skill | All types with the plugin |
| `orch:worker`, `orch:orchestrator` | Agent | Supervisor and console sessions (`--agent`) |
| `orch:docs-checker`, `orch:state-checker` | Agent | Model calls of the supervisor |

| Hook module | Job |
|---|---|
| `register.ts` | Activates the session, registers the tools, mirrors the transcript, denies `gh pr merge` and `AskUserQuestion` |
| `tools.ts` | Registers the 8 tools from the template schemas |
| `deliver.ts` | Delivers new envelopes as one turn. Live sessions only |
| `lease.ts` | Writes `lease.json` and the heartbeat |
| `stream.ts`, `events.ts` | Post envelopes and the session traces |
| `backend.ts`, `state.ts` | Store IO, shared state, the `orch` runner |

## App tools

The desktop app gives these tools to app and Remote Control turns. A supervisor turn does not have them.

| Tool | Orchestrator use |
|---|---|
| `start_session` | Only the call that `spawn_child` returns |
| `get_session` | Read a child by its full `local_<uuid>` id |
| `set_remote_control` | Only as a notice asks |
| `archive_session` | Only as a notice asks |
| `move_sessions`, `create_group` (sidebar) | Only as a notice or `orch join` asks: the Workers group |
| `send_message`, `detach_session`, `unarchive_session` | Not used |

## Lifecycle rules

### Start a child

`mcp__orch__spawn_child` is the only spawn path of an orchestrator. The skill `orch:orch-brief` has the steps.

```
Root turn          spawn_child call            Result
App          ->    inApp default         ->    one start_session call, made as given
Remote Control ->  inApp: false          ->    the supervisor starts the child
Supervisor   ->    default               ->    the supervisor starts the child
Refused      ->    again, inApp: false   ->    the supervisor starts the child
```

The supervisor opens the child in the app (notice `open-in-app`). Then the child turns its own Remote Control on (notice `remote-control-self`).

### The Workers group

Each child session in the app goes into the sidebar group `Workers`. The root and the sessions of the user stay out of it.

| Start path | Who moves the child | Text |
|---|---|---|
| The supervisor starts the child | The child, in its first app turn | Notice `remote-control-self`, fields `groupTool`, `groupCreateTool`, `group` |
| The app starts the child (`start_session`) | The child, after `orch join` | The output of `orch join` |

The child calls `move_sessions` with its full `local_<uuid>` id and the group `Workers`. If the group does not exist, it calls `create_group` first. If the tools are missing or refuse, the child continues: the user can drag the session.

### The recurring check of a root

Each live root orchestrator session gets a check about every 5 minutes (`plugin/hooks/check.ts`). The user does not ask for it, and no `CronCreate` is necessary.

1. The check runs only when the root is idle.
2. If envelopes with no ack wait (for example a child report), the deliverer delivers them.
3. Else the check reads `orch decisions`. Each open question that the root did not show yet becomes one turn: "Show each one to the user".
4. A question that reached the root as an envelope counts as shown.

### A new engine session

The app can give a session a new engine session id. The app id stays the same. The plugin then finds no folder under the new id.

| Where | How the plugin finds its store id |
|---|---|
| Supervisor turn | `ORCH_SESSION_ID`, which the supervisor sets |
| App process | `orch whoami`: the entry with the same app id |

`orch whoami` records the new id in `engine.sessionId`. A supervisor turn and `orch open` resume that id. A supervisor turn never posts an app id: it inherits the host id of the process that started the supervisor.

### Retry rules

| Result | Rule |
|---|---|
| `start_session`: "the user is working over Remote Control" | Do not try again. `spawn_child` with `inApp: false` |
| `start_session`: "only on the user's own words", "only where this one runs" | Do not try again. `spawn_child` with `inApp: false` |
| `get_session`, `set_remote_control`: "not found" | Use the full `local_<uuid>` id. Wait 5 s, try again, 3 times maximum |
| `set_remote_control`: "requires an active session" | Send the session a message first (it starts a turn), wait 5 s, try again. An immediate retry fails |
| `set_remote_control` on an archived session | `unarchive_session` first |
| Auto mode: "a hook changed this call's input" | Send the same call again, alone |
| `send_message`: "a turn is in progress" | Not a failure: the app queues the message |

### Archive a child

1. Run `orch archive <child>`. It sends the notice `archive-self` and waits up to 120 s for the app.
2. The child turns its Remote Control off, then calls `archive_session` on itself.
3. If the app says "still has live work", the child stops its background tasks and tries one more time.
4. If the app keeps the session for the user (pinned, or open on screen), no tool can archive it. The user archives it from the sidebar.
5. If a process holds the worktree folder, run `orch archive <child> --store-only`. The folder stays.
6. If a turn of the child runs, run `orch stop <child>` first.

## Non-goals

- An orchestrator does not use `core:spawn`. Reason: it gives no store entry, no brief delivery and no archive path. `core:spawn` is for normal sessions.
- No plugin path in the user settings and no plugin copy from the marketplace at the same time. Reason: two plugins with the name `orch` load two times.
