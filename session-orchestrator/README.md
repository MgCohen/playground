# Session orchestrator

A local system that controls many Claude Code sessions. A C# supervisor and a Claude Code mod share a file store. Each session has one writer at a time, decisions go to the user as free-text cards, and a default decision after a timeout lands as a stacked draft PR.

Design page: https://claude.ai/artifact/EggEYH4ivUuPGG9Pw4k1y1 (source `docs/orchestrator-design.html`).

| Folder | Content |
|---|---|
| `src/Orchestrator.Core/` | Contract records, name order, lease rule, policy, events. No file IO |
| `src/Orchestrator.Store/` | Atomic file IO, session files, the event stream |
| `src/Orchestrator.Supervisor/` | The tick, the decision policy, the `claude -p` runner |
| `src/Orchestrator.Web/` | One process: the supervisor and the HTTP API |
| `tests/Orchestrator.Tests/` | NUnit tests with a fake runner and a temporary store |
| `mod/orch/` | The Claude Code mod: inbox, heartbeat, lease, mirror, tools `ask_user`, `report`, `spawn_child` |
| `config/` | The decision policies |
| `docs/` | `plan.md`, `store.md`, `know-how.md`, `decisions.md`, the design page |
| `spikes/` | Spike scripts, test briefs, and the frozen TypeScript supervisor (`supervisor-ts/`) |
| `store/` | Runtime data. Git ignores it |

Build and test:

```bash
dotnet test --solution Orchestrator.slnx
```

Start the supervisor (HTTP API on `http://127.0.0.1:7777`):

```bash
dotnet run --project src/Orchestrator.Web
```

`ORCH_STORE` sets the store folder and `ORCH_PORT` sets the port.
