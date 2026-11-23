# Session orchestrator

Read these files before you change this project:

| File | Content |
|---|---|
| `docs/plan.md` | Goal, phases, test results, findings, next steps |
| `docs/store.md` | The store contract, the HTTP API, the policy |
| `docs/know-how.md` | Engine limits, mod rules, live test recipe, app import, settings |
| `docs/orchestrator-design.html` | The design page (https://claude.ai/artifact/EggEYH4ivUuPGG9Pw4k1y1) |
| `docs/how-it-works.html` | How it works today: processes, folders, store files, call stack, sequences (https://claude.ai/artifact/CP2ozBgysFuJSwhrgoHugt) |
| `docs/session-management.html` | The options for session management, with a worked example each (https://claude.ai/artifact/SRBob5ix2xQ2ArpwDXTJaW) |
| `docs/setup-options.html` | Install and start options, and the cloud plan (https://claude.ai/artifact/TJU9Foq3kBVJptSwMnCCeX) |

## Rules

- The store contract in `docs/store.md` is the interface between the mod and the supervisor. If you change a file in the store, change `docs/store.md` in the same commit.
- Record each test result and each finding in `docs/plan.md`.
- Keep the design page current. After a change, publish it again to the same artifact URL.
- Never use click questions. The user answers in free text or by voice.
- Test a live change in the scratchpad with its own `ORCH_STORE` and `ORCH_PORT`. Never use the real store (`%LOCALAPPDATA%\orch\store`, or the old `store/`) for tests.

## Stack

C# on .NET 10, the `claude` CLI, JSON Schema for the templates, Blazor for the UI, NUnit for the tests. Do not add TypeScript or JavaScript, with two exceptions: the plugin hooks (the Claude Code engine runs only TypeScript) and small JavaScript parts of the Blazor UI. `spikes/supervisor-ts/` is the frozen TypeScript supervisor of the spikes. Do not change it.

## Provider boundary

Follow these rules for each change. The design is on the Engines tab of `docs/session-design.html`.

1. A feature is a vertical slice. It holds its own logic (prompt, parse, rules) and no provider code.
2. If a slice needs anything that depends on a provider (a model call, a session, an app fact, a CLI command, a configuration check), it calls `IProvider`.
3. Only `Providers/<provider>/` knows the names of the provider: its executable, flags, files, tool names and environment variables.
4. A call into our system needs no provider, whoever makes it: `orch`, the API, the tray, the records, the envelopes.
5. The core contract and the store keep only references to a provider (`engine.kind`, `sessionId`, `appId`), and no provider facts.
6. If the provider gives a function, use it through `IProvider` and do not make our own copy. When code moves behind `IProvider`, keep the logic of today.

## Commands

| Job | Command |
|---|---|
| Build and test | `dotnet test --solution Orchestrator.slnx` |
| Start the supervisor | `dotnet run --project src/Orchestrator.Web` |
| Test store and port | Set `ORCH_STORE` and `ORCH_PORT` before the start |
| Install or update `orch` | `dotnet pack src/Orchestrator.Cli -c Release -o <folder>`, then `dotnet tool update -g Orchestrator.Cli --add-source <folder>` |
| Check the setup | `orch doctor` |
| Install or update the tray, the supervisor and `orch` | `.\install.ps1` (needs the approval of the user on the real profile; see `docs/know-how.md`) |
| Start or stop the supervisor | `orch supervisor start\|stop\|status [--port P]` |

## Work with the user

- Write in ASD-STE100 Simplified Technical English. Keep replies short: the answer first, about 10 to 15 lines.
- Ask questions in the reply text only. Never use a click box: no `AskUserQuestion`, and no question or buttons in `wrap_up`. The user dictates the answers.
- Ask each question in the full format: the question, the context, a snippet if useful, the options, and your recommendation. Put all open questions one below the other at the end.
- If you need a permission for project work, you have it. Ask first only for an action outside the project that you cannot undo, or one that the user did not ask for.
- In spike and test work, do not wait for the user. Run the test, then report the result.
- The user reviews defaults later. Prefer a reversible default and a record over a stop.
- Use a template for each input and output (decision, answer, review, report, update, brief, notice). Templates make the messages the same each time and make the UI easy to build. See `templates/` and the Templates tab of the design page.
- Show a folder layout as a foldable tree: one line for each folder or file, a short description, and a detail that opens under the line.

## Load the plugin

Each session gets the plugin from the `mgcohen` marketplace (`MgCohen/claude-plugins`, account-synced). To publish a change: increase `version` in `plugin/.claude-plugin/plugin.json`, run `tools/publish-plugin.ps1`, and merge the PR that it opens. The sync takes a few minutes.

- For development, start a session with `claude --plugin-dir <path of plugin/>`. A test supervisor with `ORCH_PLUGIN_DIR` does the same for its turns.
- Do not put `CLAUDE_CODE_PLUGIN_DIRS` in the user settings while the marketplace copy is on: two copies register each tool two times. `orch doctor` checks it.
- `docs/commands.md` lists each command, tool, skill and agent, and the lifecycle rules. The settings history is in `docs/know-how.md`.
