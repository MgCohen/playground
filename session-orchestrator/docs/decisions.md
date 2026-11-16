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
| 2026-11-16 | Where does a session worktree go? | `<repo>.wt/orch-<id8>`, on branch `orch/session-<id8>` | The `.wt` sibling folder is the convention of the other projects of the user (`vsa-template.wt`). A sibling folder is not inside the repository |
| 2026-11-16 | Who makes the worktree? | The supervisor, at the first turn, and it records `repo`, `branch` and the new `cwd` in `meta.json` | The open question of `plan.md` recommended the supervisor |
| 2026-11-16 | How does a stop reach a running turn? | `control.json` with `stopped`. The tick cancels the turn, and the runner kills the `claude` process tree | One file for the CLI and the API; the supervisor stays the only writer of `meta.json` |
| 2026-11-16 | Archive rules | Refuse a session with open decisions or open PRs, unless `--force`. Remove the worktree only if it is clean. Keep the branch | The M5 rules of `plan.md`: never archive a session that waits for the user, keep a child while its PR is open |
| 2026-11-16 | Restart after `maxFailures` | A cooldown of 5 min that doubles each round, then the `blocked` notice and a new turn | A session must not stay blocked until a supervisor restart, and a broken session must not retry fast |
| 2026-11-16 | Kill the grandchildren of a stopped turn with a Windows job object? | No. A test showed that the grandchild leaves the job | The host job probably allows silent breakaway. The limit is in `plan.md` |
| 2026-11-16 | `orch open` in the live test | Tested with `--dry-run` only | A real run opens the desktop app of the user while the user is away |
| 2026-11-16 | Blazor mode | Blazor Server (interactive server) in the supervisor process | The open question of `plan.md` recommended it. The UI reads the store and the supervisor state directly |
| 2026-11-16 | How does the UI stay current? | Each view reloads its data every 2 s on the server and sends the change over the Blazor circuit | No extra push code. The store has files, not change events |
| 2026-11-16 | Names of the template cards | `DecisionCard` and `DefaultCard` in the queue. `<Kind>Fields` cards (for example `ReportFields`) in the timeline | The queue cards take input; the timeline cards only show the fields of one event |
| 2026-11-16 | Swipe on a decision card | Right accepts the default, left focuses the free-text field. 25 lines of JavaScript in `wwwroot/swipe.js` | The open question of `plan.md`. The stack allows small JavaScript parts in the UI |
| 2026-11-16 | Voice input | Phone dictation in the text field. No extra code | The open question of `plan.md` |
| 2026-11-16 | **Blocked**: Cloudflare Tunnel and Cloudflare Access | Not made | They need a Cloudflare account and a domain of the user |
| 2026-11-16 | When does remote access come? (user) | Cloudflare, as the last step after all other work. Until then, tests run only on this PC | The user decided it |
| 2026-11-16 | Sign-in without the tunnel | `ORCH_TOKEN`: open the page once with `?token=<token>` (a cookie for 30 days), or send the header `X-Orch-Token`. With no `ORCH_TOKEN`, the UI is open on 127.0.0.1 | The open question of `plan.md` asked for a token header with Cloudflare Access. The token works alone on a private network too |
| 2026-11-16 | Address for a phone on the local network | `ORCH_BIND` (default `127.0.0.1`). `ORCH_BIND=0.0.0.0` with `ORCH_TOKEN` serves the phone on the same Wi-Fi | A step before the tunnel, with no account |
| 2026-11-16 | The phone test | The browser pane of the desktop app at 375 × 812, not a real phone | A real phone needs the tunnel or a network address, and the user is away |
| 2026-11-16 | **Blocked**: M5 on the real vsa-template repository | Not run. The stream ran on a private copy, `MgCohen/orch-m5-vsa-copy`, from start to merge | `plan.md` says to ask the user which stream runs first, and a merge into the real repository is an action outside this project that the user did not approve |
| 2026-11-16 | Which stream for the M5 run? | Issue #140 (a validator is only in the slice of its handler) | A stable-roadmap check with a clear done test and a small scope |
| 2026-11-16 | Timeouts of the M5 run | low 90 s, medium 120 s, high never, docs check on low and medium | The defaults of 30 min and 4 h are too long for an unattended test |
| 2026-11-16 | Keep the copy repository `orch-m5-vsa-copy`? | Yes, private. It holds a candidate implementation of #140 (merged PR #3) and two default PRs for review | A delete cannot be undone, and the user can use the work for the real issue |
| 2026-11-16 | A merge of a PR stacked on default branches | New rule in the `orchestrator` agent: merge only after the user reviewed the defaults, or when the brief allows it | The M5 run merged PR #3, and with it the commits of the two unreviewed defaults |
| 2026-11-16 | Store version | `store/version.json`, version 1. A newer store stops the supervisor at start | `plan.md`, M6 |
| 2026-11-16 | How does the supervisor find an interrupted turn? | `turn.json` gets `ended`. A marker with no `ended` and no running turn is interrupted. A `session.input` event with `refs.turnStart` of that turn shows that the prompt arrived | The plugin already links each input to its turn (M2), so the stream has the proof |
| 2026-11-16 | Form of the end-to-end script | A C# console app, `tests/Orchestrator.E2E` (`dotnet run --project tests/Orchestrator.E2E`). Not part of `dotnet test` | It starts real `claude` sessions and takes minutes. The stack allows no new script language |
