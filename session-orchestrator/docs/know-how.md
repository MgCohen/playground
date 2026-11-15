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

## Mod rules

- Write each helper as a top-level `function` that takes `$`. The validator refuses arrow-function helpers.
- Read an environment variable with `$.env.get('LITERAL')`, with a literal name.
- Give each promise a `.catch` handler.
- `tool.describe` can change the description of a tool and move it behind ToolSearch (`isDeferred`).
- `tsc` gives TS2589 on `on('tool.call', { tool: 'mcp__...' })`. This is a limit of the type checker. The mod loads correctly.
- The engine writes `mod/orch/.claude-plugin/types/` when it loads the mod. Git ignores that folder. `tsconfig.json` needs it, so `tsc` works only after one load.
- Check the mod with `claude plugin validate mod/orch`.

## Node and the SDK

- Node 26 runs `.ts` files directly (type stripping). There is no build step.
- npm scripts fail, because the cmd shell of npm cannot find `node`. Run `node --test test/` and `node src/main.ts`.
- The Agent SDK (`@anthropic-ai/claude-agent-sdk` 0.3.325) uses the local login of the subscription (`apiKeySource: none`). No API key.
- The supervisor sets `ORCH_HOLDER=supervisor` and `ORCH_STORE` for each SDK turn. `ORCH_PORT` sets the HTTP port (default 7777).

## Live test recipe

1. Make a git repository in the scratchpad. Put an `AGENTS.md` with one rule in it, for the docs check.
2. If the test needs PRs, make a private GitHub repository with `gh repo create --private --source . --push`. Delete it after the test.
3. Copy `config/policy.test.json` into a new store folder as `policy.json` (medium: 30 s).
4. Start the supervisor in the background: `ORCH_STORE=<store> ORCH_PORT=7779 node src/main.ts` in `supervisor/`.
5. Change the `cwd` in `spikes/p3/root.json` (one repository) or `spikes/p4/root.json` (PR stack), then send it: `curl -X POST -H 'content-type: application/json' --data @root.json http://127.0.0.1:7779/api/sessions`.
6. Play the user with `POST /api/decisions/<session>/<id>` (answer) and `.../review` (`approve`, `change`, `refuse`).
7. Watch `answers/`, `report.json` and `blocked.json` of the session. A POST starts a tick at once, so two close reviews run as two turns.
8. Examine the ledger, the git graph and the PRs. Stop the supervisor.

## App sessions

- To open a supervisor session in the desktop app, run `claude.exe --desktop --resume <id>` with PowerShell `Start-Process`. From bash it gives exit 1.
- Then send one message to the session (`send_message`) to start its process. `set_remote_control` makes it visible on the phone.
- The app keeps transcripts under `~/.claude/projects/<folder key>/`, keyed to the folder of the session. The spike sessions are under the keys of `claude-orchestrator/spikes`. A resume from another folder may not find them (not tested).

## Settings history

| File | State |
|---|---|
| `~/.claude/settings.json` | Mod env lines, `defaultMode: auto`, `deny: ["AskUserQuestion"]` |
| `settings.json.bak-askuser` | Before the deny rule (mod path of `claude-orchestrator`) |
| `settings.json.bak-bypass` | Older state, with no mod lines and no `defaultMode` |
