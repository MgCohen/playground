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
