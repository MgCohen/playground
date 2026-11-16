# Decisions

The build session made these decisions while the user was away. The user reviews them later. Each row has the date, the question, the choice and the reason. A row with the state **blocked** needs the user.

| Date | Question | Choice | Reason |
|---|---|---|---|
| 2026-11-16 | Does `claude -p` work from C# with a fixed session id? | Yes. `--session-id` starts the session, `--resume` continues it, `--output-format stream-json --verbose` streams one JSON object for each line, and the mod loads (lease and mirror rows in the test store) | Spike `spikes/m0-cli`: turn 2 knew the word KIWI from turn 1 |
| 2026-11-16 | How does the runner give the prompt to `claude -p`? | On standard input, not as an argument | Long briefs and quotes break the Windows command line |
| 2026-11-16 | Which setting sources does a supervisor turn use? | `--setting-sources user`, the same as the SDK runner | Keeps the behavior of the TypeScript supervisor |
| 2026-11-16 | How does the docs check run? | `claude -p --no-session-persistence --max-turns 12`, with only Read, Glob and Grep | The check is read-only and must not add sessions to the app list |
| 2026-11-16 | How do the tests control time? | A `TimeProvider` (`ManualClock` in the tests) | Standard .NET type, no extra package |
| 2026-11-16 | Event first or state file first? | The event first, then the state file | `plan.md`, stream rules |
| 2026-11-16 | Thread safety of the tick | One lock around the tick. A finished turn leaves the running list under the same lock | The timer and the HTTP calls both start a tick |
| 2026-11-16 | Where does the TypeScript supervisor go after the port? | `spikes/supervisor-ts/`, frozen. Its 16 tests still pass there | The design page, Build tab: "moves here after the port" |
| 2026-11-16 | Which GitHub repository for the PR stack test? | A private test repository for each run, deleted after the run | `know-how.md`, live test recipe |
| 2026-11-16 | Which JsonSchema.Net version? | 9.7.0, the newest. A probe showed that `JsonSchema.FromText` and `Evaluate(JsonElement)` work | The plan names JsonSchema.Net, with no version |
| 2026-11-16 | System.CommandLine for `orch`? | No. A small parser of my own (`Args.cs`) | The package is at 3.0 with a new API, and the command set is small |
| 2026-11-16 | Template syntax of `render.md` | `{{field}}` and `{{#field}}...{{/field}}` (shown only when the field has a value). A list shows as "a, b" | The simplest syntax that covers the optional fields |
| 2026-11-16 | Does `Orchestrator.Core` read the template files? | Yes, `TemplateSet` reads `templates/` | The design puts validate and render in Core. The rest of Core has no IO |
| 2026-11-16 | Where are the PR texts? | Extra renders of the decision template: `render/pr-title.md`, `pr-body.md`, `pr-close.md`, `pr-close-above.md` | A PR shows one decision, so its texts belong to that template |
| 2026-11-16 | Where are the stack versions? | `sessions/<id>/stack/<decision>.json`, written by `orch` | One writer: the session that runs `orch` in its folder |
| 2026-11-16 | How does `orch` find the session of a decision? | It scans `decisions/` and `stack/` of each session for the id | A session does not know its own id in a turn |
| 2026-11-16 | `ledger.jsonl` | Removed. The ledger is the query "answer from docs or policy, or review" of the event stream | `plan.md`, stream rules |
| 2026-11-16 | How does a session run `orch`? | A global .NET tool: `dotnet pack src/Orchestrator.Cli`, then `dotnet tool install -g Orchestrator.Cli --add-source <folder>`. Each build embeds the repository path, so `orch` finds `templates/` and `config/` | Sessions run `orch` in their own folders. `~/.dotnet/tools` is on the PATH. To remove: `dotnet tool uninstall -g Orchestrator.Cli` |
| 2026-11-16 | The sender of an input in a supervisor turn | Each inbox file has `from`. The supervisor writes `turn.json` with the senders before the turn, and the mod reads it | The fix of the event spike: "take the sender from the `turn.start` before it" |
| 2026-11-16 | **Blocked**: the mod side of the event fixes (no empty `session.output`, `session.tool`, the sender from `turn.json`, the report event id) | Not in `mod/orch`. Auto mode could not evaluate edits to `mod/orch/hooks/register.ts` (no verdict), so the file is unchanged. The fixes go into the new plugin of M2 | The mod loads in each session of the user |
| 2026-11-16 | Can a plugin hook import a relative `.ts` file? | Yes. `plugin/hooks/` has five modules | Spike in the scratchpad: `helper.ts` ran at `session.start` |
| 2026-11-16 | Does `CLAUDE_CODE_PLUGIN_DIRS` give plugin skills and agents to a session? | Yes, the same as `--plugin-dir`. Names start with `orch:` | Spike, then a check after the settings change |
| 2026-11-16 | How does `spawn_child` make a child? | The plugin runs `orch new --parent <id> --created-by spawn_child`. The brief rules stay in C# | The design: "for other rules, the plugin calls orch" |
| 2026-11-16 | How does a test supervisor load the plugin without the settings? | `ORCH_PLUGIN_DIR`: the runner uses `--setting-sources project,local --permission-mode auto --plugin-dir <dir>` | The settings can stay unchanged in a test. Auto mode refused `bypassPermissions`, so the runner uses `auto`, the mode of the user |
| 2026-11-16 | Settings change of M2 | `CLAUDE_CODE_PLUGIN_DIRS` points to `plugin/`. Backup: `~/.claude/settings.json.bak-m2-plugin` | The plan: "M2 moves it here". To undo: copy the backup back |
| 2026-11-16 | Delete `mod/orch/`? | Not yet. It stays until each open session that loaded it ends | The open sessions of the user may use it with hot reload |
| 2026-11-16 | `plugin/commands/orch-enable.md` | Not made. The hook registers `/orch-enable` at `session.start`, the same as before | A Markdown command cannot write the `enabled` file. The registered command can |
| 2026-11-16 | How does a brief name an agent? | The brief field `agent` (for example `worker`). The supervisor adds `--agent orch:<agent>` to each turn | The spike showed the `orch:` names |
