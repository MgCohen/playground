# Build report: M0 to M6

All seven milestones are built, tested and merged into `main`. M5 ran on a private copy of vsa-template, not on the real repository, because the choice of the stream belongs to the user. Three items wait for the user: the tunnel, the M5 run on the real repository, and the review of five decisions in `decisions.md`.

Date: 2026-11-16. Branches: one for each milestone (`orch/m0-port` to `orch/m6-stable`), each merged with `--no-ff` and pushed.

## Milestones

| Milestone | Delivered | Done check | Commits |
|---|---|---|---|
| M0 Port | C# solution: Core, Store, Supervisor, Web API, `claude -p` runner. Spike `spikes/m0-cli` | ✅ 16 tests in NUnit, PR stack live test | `a2bab3c`, `5a5029b`, merge `ffd918c` |
| M1 Messages | 7 templates, event query, the `orch` command, stack commands, decision versions, `[orch]` PR texts | ✅ The PR stack test runs on `orch` commands | `e7b412a`, `62acbbb`, `f2c9a43`, merge `d169e7d` |
| M2 Plugin | `plugin/`: 5 hook modules, tool schemas from the templates, 4 skills, 3 agents, the event fixes | ✅ A five-line brief that names skills gave three stacked PRs | `8126d3d`, `93cb437`, merge `b502407` |
| M3 Lifecycle | Stop, resume, archive, worktrees, restart after failures, `orch open` | ✅ Two code sessions changed one file in one repository | `0c768e7`, `a28ead1`, merge `1fc6aaf` |
| M4 UI | Blazor Server: queue, sessions, timeline, cards, swipe, token sign-in | ✅ Answer and review from the phone layout | `e9beb13`, `5e04440`, `4ccdf55`, merge `0e95047` |
| M5 Stream | Issue #140 of vsa-template, start to merge, on the copy `MgCohen/orch-m5-vsa-copy` | ✅ on the copy: PR #3 merged, 2 of 2 decisions delivered | `bc4074d`, merge `ea9b2f4` |
| M6 Stable | Crash recovery, store version, end-to-end script | ✅ Two supervisor crashes lose nothing | `8bbe577`, merge `91165dd` |

The details of each result are in `plan.md` (sections "M0 result" to "M6 result").

## Tests

| Test | Result |
|---|---|
| `dotnet test --solution Orchestrator.slnx` | ✅ 62 of 62 pass |
| `dotnet run --project tests/Orchestrator.E2E` | ✅ 8 of 8 checks pass, with two supervisor crashes |
| `node --test test/` in `spikes/supervisor-ts/` | ✅ 16 of 16 pass (frozen TypeScript version) |
| Live: PR stack on GitHub (M0, M1, M2) | ✅ Three test repositories, deleted after each run |
| Live: two worktrees, stop (M3) | ✅ |
| Live: phone layout in the browser pane (M4) | ✅ |
| Live: vsa-template stream on the copy (M5) | ✅ Architecture.Tests 164 and Engine.Tests 22 pass on the merged PR |

## Decisions

Each decision has one row in [`decisions.md`](decisions.md): 55 rows. The rows below change something outside the code, or need a review first.

| Decision | Undo |
|---|---|
| `~/.claude/settings.json` loads `plugin/` instead of `mod/orch` | Copy `settings.json.bak-m2-plugin` back |
| `orch` 1.0.0 is a global .NET tool | `dotnet tool uninstall -g Orchestrator.Cli` |
| The private repository `MgCohen/orch-m5-vsa-copy` stays (candidate work for #140) | `gh repo delete MgCohen/orch-m5-vsa-copy` |
| `mod/orch/` stays until the open sessions that loaded it end | Delete the folder later |
| The `orchestrator` agent merges a PR stacked on defaults only after their review | Edit `plugin/agents/orchestrator.md` |

## Blocked

| Item | Why | Next step for the user |
|---|---|---|
| Cloudflare Tunnel and Access (M4) | They need a Cloudflare account and a domain | Give the account, or use `ORCH_BIND=0.0.0.0` with `ORCH_TOKEN` on the home network |
| M5 on the real vsa-template repository | `plan.md` says the user picks the stream, and a merge into that repository needs approval | Pick the stream. Issue #140 has a candidate in PR #3 of the copy |
| Mod side of the M1 event fixes in `mod/orch` | Auto mode could not evaluate edits to the live mod file | None: `plugin/` has the fixes and the settings load it |
| `orch open` live run | It opens the desktop app while the user is away | Run `orch open <id>` once |

## Known limits

- A stop ends the `claude` process tree, but a grandchild of a tool can live on and lock a worktree folder. A Windows job object did not catch it in this host.
- The phone test used the browser pane at 375 × 812, not a real phone.
- `/orch-enable` is not tested in the app.

## Next steps

1. Review the five decisions of `decisions.md` that change something outside the code (table above).
2. Pick the first real vsa-template stream for M5. Start it with `orch new --agent orchestrator --skills orch-brief,orch-ask,orch-report`.
3. Decide about the tunnel: Cloudflare, or the home network with a token.
4. Start the supervisor for daily use: `dotnet run --project src/Orchestrator.Web`, then open `http://127.0.0.1:7777/`.
5. Delete `mod/orch/` after the next restart of the app.
