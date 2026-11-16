# Know-how

Facts about the engine, the mod, the tests and this computer. The test results that prove them are in `plan.md`.

## Engine limits

| Limit | Rule |
|---|---|
| Hook budget | A `tool.call` hook stops after about 10 s. A `$` call in flight does not count |
| Long wait in a tool | Wait inside `$.process.run`. Its `timeoutMs` is 600 000 maximum, so cap it with `Math.min` |
| `$.prompt.submit` | Resolves when its turn starts, not when it ends. Submit only when the session is idle |
| Stop latch | After the user presses Stop, the engine refuses each plugin submit until the person types. A peer message does not clear it |
| Mod reload | A reload runs `register` and `session.start` again. Module variables start over, so load the state from the store |
| `session.end` | Fires when the app archives a session or an idle app process ends (after about 3 h 45 min in S11) |
| Two writers | Two processes that write one session split the transcript. Two processes that write one log file lose lines |

## Plugin rules

- Write each helper as a top-level `function` that takes `$`. The validator refuses arrow-function helpers.
- Read an environment variable with `$.env.get('LITERAL')`, with a literal name.
- Give each promise a `.catch` handler.
- `tool.describe` can change the description of a tool and move it behind ToolSearch (`isDeferred`).
- A hook module can import a relative `.ts` file (`./state.ts`). The modules of one plugin share their module state.
- Plugin skills and agents reach each session through `CLAUDE_CODE_PLUGIN_DIRS` and through `--plugin-dir`. Their names start with `orch:` (`orch:orch-ask`, `orch:worker`).
- `claude -p --agent orch:worker` starts a session with a plugin agent.
- `$.process.run(argv, { cwd, env, stdin, timeoutMs })` runs a program with no shell. The child gets the environment of the session, so `orch` gets `ORCH_STORE`.
- Auto mode refuses `--permission-mode bypassPermissions` in tests. Use the user settings, or `--permission-mode auto`.
- `tsc` gives TS2589 on `on('tool.call', { tool: 'mcp__...' })`. This is a limit of the type checker. The plugin loads correctly.
- The engine writes `plugin/.claude-plugin/types/` when it loads the plugin. Git ignores that folder. `tsconfig.json` needs it, so `tsc` works only after one load.
- Check the plugin with `claude plugin validate plugin`.

## Node and the SDK (spikes only)

- Node 26 runs `.ts` files directly (type stripping). There is no build step.
- npm scripts fail, because the cmd shell of npm cannot find `node`. Run `node --test test/` in `spikes/supervisor-ts/`.
- `spikes/sdk` has no `node_modules`. Run `npm i` there before `s5.mjs`, `s6.mjs` or `s9.mjs`.
- The Agent SDK (`@anthropic-ai/claude-agent-sdk` 0.3.325) uses the local login of the subscription (`apiKeySource: none`). No API key.
- The supervisor sets `ORCH_HOLDER=supervisor` and `ORCH_STORE` for each SDK turn. `ORCH_PORT` sets the HTTP port (default 7777).

## Live test recipe

1. Make a git repository in the scratchpad. Put an `AGENTS.md` with one rule in it, for the docs check.
2. If the test needs PRs, make a private GitHub repository with `gh repo create --private --source . --push`. Before you delete it, copy the PR bodies and comments into `plan.md`: GitHub holds the only copy. The user allows you to create and delete test repositories without a question. The user also allows a public test repository if a private one is blocked.
3. Copy `config/policy.test.json` into a new store folder as `policy.json` (medium: 30 s).
4. Publish a copy (`dotnet publish src/Orchestrator.Web -o <scratchpad>/sup`), then start it in the background: `ORCH_STORE=<store> ORCH_PORT=7779 dotnet <scratchpad>/sup/Orchestrator.Web.dll`. A supervisor that runs from `bin/` locks the DLLs, and the next build fails. The copy finds the repository through the path that the build embeds.
5. Copy `spikes/p3/root.json` (one repository) or `spikes/p4/root.json` (PR stack) to the scratchpad, change its `cwd`, then send it: `curl -X POST -H 'content-type: application/json' --data @root.json http://127.0.0.1:7779/api/sessions`.
6. Play the user with `orch answer <decision> <text>` and `orch review <decision> approve|change|refuse <text>`, with `ORCH_STORE` set to the test store.
7. Watch `answers/`, `report.json` and `blocked.json` of the session. A POST starts a tick at once, so two close reviews run as two turns.
8. Examine the ledger, the git graph and the PRs. Stop the supervisor.

## App sessions

- To open a supervisor session in the desktop app, run `claude.exe --desktop --resume <id>` with PowerShell `Start-Process`. From bash it gives exit 1.
- Then send one message to the session (`send_message`) to start its process. `set_remote_control` makes it visible on the phone.
- The app keeps transcripts under `~/.claude/projects/<folder key>/`, keyed to the folder of the session. The spike sessions are under the keys of their folders, for example `claude-orchestrator-spikes-p2`. `dee17212` is under a scratchpad key of the build session. A resume from another folder may not find them (not tested).

## Settings history

| File | State |
|---|---|
| `~/.claude/settings.json` | Plugin env lines (`plugin/`), `defaultMode: auto`, `deny: ["AskUserQuestion"]` |
| `settings.json.bak-m2-plugin` | Before M2: the env line pointed to `mod/orch` |
| `settings.json.bak-askuser` | Before the deny rule (mod path of `claude-orchestrator`) |
| `settings.json.bak-bypass` | Older state, with no mod lines and no `defaultMode` |

## The claude CLI from C#

- `claude -p --output-format stream-json --verbose` writes one JSON object on each line. The last line has `type: result`, `subtype: success` and `result` (the last text).
- `--session-id <uuid>` starts a session with that id. `--resume <uuid>` continues it.
- Give the prompt on standard input. A long brief as an argument breaks on the Windows command line.
- At the end of a `claude -p` process, `session.end` fires, so the mod sets the lease to `none`.
- The process inherits `ORCH_HOLDER` and `ORCH_STORE` from the runner, so the mod writes to the test store.

## The orch tool

- Install or update it after a change of `src/Orchestrator.Cli`: `dotnet pack src/Orchestrator.Cli -c Release -o <folder>`, then `dotnet tool update -g Orchestrator.Cli --add-source <folder>` (`install` the first time).
- The build embeds the repository path, so `orch` finds `templates/` and `config/` from any folder. `ORCH_HOME` overrides it.
- `orch` uses `ORCH_STORE`. A supervisor turn sets it, so a session that runs `orch` writes to the store of its supervisor.
- `orch doctor` checks the home, the store, the policy, the templates, `claude`, `gh`, the plugin path and the supervisor.

## Session lifecycle

- `orch stop <id>` ends a running turn: the supervisor cancels it and kills the `claude` process tree. A grandchild of a tool can outlive it and keep files locked until it ends.
- `orch archive <id>` removes the `enabled` file. The supervisor never runs the session again. The branch of its worktree stays.
- A worktree folder is `<repo>.wt/orch-<id8>`, next to the repository, on branch `orch/session-<id8>`.
- `orch open <id>` opens a session in the desktop app. Then send it one message in the app, so that its process starts and the plugin takes the lease.
