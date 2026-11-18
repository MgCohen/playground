# Session orchestrator

A local system that controls many Claude Code sessions. A C# supervisor and a Claude Code mod share a file store. Each session has one writer at a time, decisions go to the user as free-text cards, and a default decision after a timeout lands as a stacked draft PR.

Design page: https://claude.ai/artifact/EggEYH4ivUuPGG9Pw4k1y1 (source `docs/orchestrator-design.html`).

| Folder | Content |
|---|---|
| `src/Orchestrator.Core/` | Contract records, name order, lease rule, policy, events. No file IO |
| `src/Orchestrator.Store/` | Atomic file IO, session files, the event stream |
| `src/Orchestrator.Supervisor/` | The tick, the decision policy, the `claude -p` runner |
| `src/Orchestrator.Web/` | One process: the supervisor, the HTTP API and the Blazor Server UI (queue, sessions, timeline) |
| `src/Orchestrator.Cli/` | The `orch` command (a .NET tool): sessions, decisions, stack, events, templates, doctor |
| `templates/` | One folder for each message kind: `schema.json`, `render.md`, `example.json` |
| `tests/Orchestrator.Tests/` | NUnit tests with a fake runner and a temporary store |
| `tests/Orchestrator.E2E/` | The live end-to-end script with two supervisor crashes |
| `plugin/` | The Claude Code plugin: hooks (inbox, heartbeat, lease, mirror, events), the tools `ask_user`, `report`, `update`, `spawn_child`, the `orch-*` skills and three agents |
| `mod/orch/` | The first mod. The settings do not load it. Keep it until each open session that loaded it ends |
| `config/` | The decision policies |
| `docs/` | `plan.md`, `store.md`, `know-how.md`, `decisions.md`, the design page |
| `spikes/` | Spike scripts, test briefs, and the frozen TypeScript supervisor (`supervisor-ts/`) |
| `store/` | The old store folder, before H4. Git ignores it. The store is now in `%LOCALAPPDATA%\orch\store`: run `orch store migrate` once |

Build and test:

```bash
dotnet test --solution Orchestrator.slnx
```

Start the supervisor and the UI (`http://127.0.0.1:7777/`, API under `/api`):

```bash
dotnet run --project src/Orchestrator.Web
```

`ORCH_STORE` sets the store folder (default `%LOCALAPPDATA%\orch\store`) and `ORCH_PORT` the port. `ORCH_TOKEN` adds a token sign-in, and `ORCH_BIND=0.0.0.0` serves the UI to a phone on the same network (use it only with `ORCH_TOKEN`).
