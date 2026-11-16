# Session orchestrator

A local system that controls many Claude Code sessions. A supervisor and a mod share a file store. Each session has one writer at a time, decisions go to the user as free-text cards, and a default decision after a timeout lands as a stacked draft PR.

Design page: https://claude.ai/artifact/EggEYH4ivUuPGG9Pw4k1y1 (source `docs/orchestrator-design.html`).

| Folder | Content |
|---|---|
| `mod/orch/` | The Claude Code mod: inbox, heartbeat, lease, mirror, tools `ask_user`, `report`, `spawn_child` |
| `supervisor/` | The Node supervisor: scheduler, Agent SDK turns, decision policy, HTTP API |
| `config/` | The default decision policy |
| `docs/` | `plan.md` (phases, test results, next steps), `store.md` (store contract and API), `know-how.md` (engine limits, test recipe, settings), the design page |
| `spikes/` | The spike scripts of P0 to P2, and the test briefs of the live tests (`p3/`: one repository, `p4/`: the PR stack test of P3) |
| `mod/orch-spike/` | The P0 spike mod. The settings do not load it. Keep it only as a reference |
| `ui/` | P4: the phone UI, served by `supervisor/src/http.ts`. Not made yet |
| `store/` | Runtime data. Git ignores it |

Start the supervisor:

```bash
cd supervisor && npm install && node src/main.ts
```

`npm install` works. The npm scripts (`npm test`, `npm start`) fail, because the cmd shell of npm cannot find `node`. To stop a supervisor that runs in the background, stop its process (Ctrl+C in a terminal, or stop the background task).

```bash
cd supervisor && node --test test/
```
