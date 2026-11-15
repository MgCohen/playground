# Session orchestrator

A local system that controls many Claude Code sessions. A supervisor and a mod share a file store. Each session has one writer at a time, decisions go to the user as free-text cards, and a default decision after a timeout lands as a stacked draft PR.

Design page: https://claude.ai/artifact/EggEYH4ivUuPGG9Pw4k1y1 (source `docs/orchestrator-design.html`).

| Folder | Content |
|---|---|
| `mod/orch/` | The Claude Code mod: inbox, heartbeat, lease, mirror, tools `ask_user`, `report`, `spawn_child` |
| `supervisor/` | The Node supervisor: scheduler, Agent SDK turns, decision policy, HTTP API |
| `config/` | The default decision policy |
| `docs/` | `plan.md` (phases and test results), `store.md` (store contract and API), the design page |
| `spikes/` | The spike scripts of P0 to P2 |
| `store/` | Runtime data. Git ignores it |

Start the supervisor:

```bash
cd supervisor && npm install && node src/main.ts
```
