# Session orchestrator

Read these files before you change this project:

| File | Content |
|---|---|
| `docs/plan.md` | Goal, phases, test results, findings, next steps |
| `docs/store.md` | The store contract, the HTTP API, the policy |
| `docs/know-how.md` | Engine limits, mod rules, live test recipe, app import, settings |
| `docs/orchestrator-design.html` | The design page (https://claude.ai/artifact/EggEYH4ivUuPGG9Pw4k1y1) |

## Rules

- The store contract in `docs/store.md` is the interface between the mod and the supervisor. If you change a file in the store, change `docs/store.md` in the same commit.
- Record each test result and each finding in `docs/plan.md`.
- Keep the design page current. After a change, publish it again to the same artifact URL.
- Never use click questions. The user answers in free text or by voice.
- Test a live change in the scratchpad with its own `ORCH_STORE` and `ORCH_PORT`. Never use `store/` for tests.

## Stack

C# on .NET 10, the `claude` CLI, JSON Schema for the templates, Blazor for the UI, NUnit for the tests. Do not add TypeScript or JavaScript, with two exceptions: the plugin hooks (the Claude Code engine runs only TypeScript) and small JavaScript parts of the Blazor UI. `spikes/supervisor-ts/` is the frozen TypeScript supervisor of the spikes. Do not change it.

## Commands

| Job | Command |
|---|---|
| Build and test | `dotnet test --solution Orchestrator.slnx` |
| Start the supervisor | `dotnet run --project src/Orchestrator.Web` |
| Test store and port | Set `ORCH_STORE` and `ORCH_PORT` before the start |
| Install or update `orch` | `dotnet pack src/Orchestrator.Cli -c Release -o <folder>`, then `dotnet tool update -g Orchestrator.Cli --add-source <folder>` |
| Check the setup | `orch doctor` |

## Work with the user

- Write in ASD-STE100 Simplified Technical English. Keep replies short: the answer first, about 10 to 15 lines.
- Ask questions in the reply text only. Never use a click box: no `AskUserQuestion`, and no question or buttons in `wrap_up`. The user dictates the answers.
- Ask each question in the full format: the question, the context, a snippet if useful, the options, and your recommendation. Put all open questions one below the other at the end.
- If you need a permission for project work, you have it. Ask first only for an action outside the project that you cannot undo, or one that the user did not ask for.
- In spike and test work, do not wait for the user. Run the test, then report the result.
- The user reviews defaults later. Prefer a reversible default and a record over a stop.
- Use a template for each input and output (decision, answer, review, report, update, brief, notice). Templates make the messages the same each time and make the UI easy to build. See `templates/` and the Templates tab of the design page.
- Show a folder layout as a foldable tree: one line for each folder or file, a short description, and a detail that opens under the line.

## Load the mod

`~/.claude/settings.json` loads the mod for all sessions with two `env` lines: `CLAUDE_CODE_PLUGIN_DIRS` (the absolute path of `mod/orch`) and `CLAUDE_CODE_PLUGIN_DIR_WATCH=1` (hot reload). To remove the mod, delete the two lines. The settings history is in `docs/know-how.md`.
