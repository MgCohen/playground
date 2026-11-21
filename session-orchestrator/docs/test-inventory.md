# Tray test inventory (2026-11-21)

A read-only review of the test code, the run records (`%LOCALAPPDATA%\Orchestrator.Tray\runs`) and the result files of this review and of R1. A time marked "estimate" is the sum of the fixed waits in the code, not a measurement.

## Result

The main cost is the run policy, not the checks. In R1, each step ran a full self-test (about 45 s) and 1 to 4 new recordings (about 25 s each): 21 self-tests and 31 recordings, about 29 min. Few tray checks can move to unit tests, because most need a real WebView2 page. The largest savings:

- Record fewer times: one approved baseline for each motion, a new recording only when that motion look changes.
- Record several scenarios in one run.
- Merge two pairs of checks in the self-test.

With the rule in part 4, R1 would have used about 16 recordings and 15 self-tests: about 13 min instead of 29 min.

## 1. Inventory

### 1a. Self-test checks (`--selftest`): 39, plus 1 with `--reduced`, plus 4 with `--mouse`

| # | Check | What it proves | Flows | Time (estimate) | Issue |
|---|---|---|---|---|---|
| 1 | `start` | The panel opens at its home place, stage `is-open` | start | 1–2 s | – |
| 2 | `label` | The run label: correct text, visible, inside the panel, not covered | start | ~0 | I17 |
| 3 | `ring-count-up` | The ring count animation rule is valid | start | ~0 | – |
| 4 | `home-ball` | The fold clip center = the center of the home button | start | ~0 | – |
| 5 | `sheet-in-panel` | The scrim covers the panel only | tabs, sheet | 0.8 s | – |
| 6 | `keys-type` | Key messages type into the reply box | sheet | 0.5 s | I5 |
| 7 | `keys-tab` | Tab moves the focus to `.mic` | sheet | 0.3 s | I5 |
| 8 | `sheet-close` | A scrim click removes the sheet | sheet | 0.3 s | – |
| 9 | `header-path-motion` | The title animation during a push | push | in 11 | – |
| 10 | `push-data` | Data only, always passes | push | 0 | – |
| 11 | `page-move-in-panel` | A push does not draw in the window margin | tabs, push | 2.2 s | – |
| 12–16 | `tab-X-to-Y` (5) | The panel shadow does not change in a tab move | tabs | ~9 s | – |
| 17–22 | `dot-X` (6) | The nav dot is under the current tab | tabs (same 5 tabs again) | 4.2 s | – |
| 23 | `badge-in-ended` | The badge slide ended at phase Badge | dock | in 24 | H2 |
| 24 | `dock` | Fold, badge place, right edge | dock | 1.2 s | – |
| 25 | `badge-hover-fits` | CSS hover width ≤ C# click area. **Fails each run** | badge | ~0 | I7 |
| 26 | `open-from-badge` | The panel is back at its place, the badge is hidden | open | 1.5 s | – |
| 27 | `hide` | Hide with no badge | hide | 0.3 s | – |
| 28 | `notice` | A new card when hidden: badge, peek, click area | notice | **6–7 s** (waits for the 5 s peek) | – |
| 29 | `saved-place` | The panel opens at a saved place | open | 1.1 s | – |
| 30 | `pin` | The pin sets TopMost | none | 0 | – |
| 31 | `badge-input` | Badge hover and click with window messages; the click opens | dock, badge, open | 2.4 s | I8 |
| 32 | `deck-throw` | The throw ends "by end", the top card changes, the tuck runs | tabs, deck | 3–6 s | S8 |
| 33 | `hide-during-motion` | A wait ends "hidden" when the tray hides during a motion | hide, open | 2.1 s | R1 review |
| 34 | `notice-while-busy` | A card during a hide gives badge and peek after the hide | hide, notice, open | 2.1 s | H4 |
| 35 | `fold-no-app-bar` | The fold on an event page takes 340–900 ms | push, dock, open | 3.5 s | H1 |
| – | `reduced-flow-time` (`--reduced`) | Dock and open < 300 ms with reduced motion | dock, open | ~1.5 s (full run ~38 s) | I13 |
| 36 | `steps-traced` | Each registry step ends "by end" | all | 0 | S4 |
| 37 | `window-steps` | Each window step has its event, in order | all | 0 | S9 |
| 38 | `no-wait-at-limit` | No wait ends by its limit or with an error | all | 0 | I12 |
| 39 | `exceptions` | No unhandled exception | all | 0 | H3 |
| M1–M4 | `--mouse` checks | Real pointer: caption drag, hover, badge drag and click | drag, badge | ~5 s. **Moves the pointer** | I8 |

Sum of the fixed waits: about 41 s, plus about 5 s for the start. Measured: 47.8 s.

### 1b. Scenarios (`--scenario`): 12

No scenario has its own pass rule: exit 1 only for a fault, a missed press or a label failure.

| Scenario | What it proves | Tray span | Full `scenario.sh` | Issue |
|---|---|---|---|---|
| `status-bubble` | No status bubble at a link click (video only) | 7.8 s | 13.5–25 s | I14 |
| `late-press` | A press 100 ms after a tab click still hits | 8.3 s | ~20 s | I3 |
| `press-rules` | Normal, late (warning) and missing (miss) press. **Exit 1 by design** | 5.3 s | not recorded | I3 |
| `keys` | Spike: which window gets posted keys | 4.0 s | not recorded | I5 |
| `dock-open` | Dock and open ×3 | 9.1 s | 22–30 s | H2, I6, I9 |
| `dock-open-reduced` | The same, reduced motion | 6.1 s | ~20 s | I13 |
| `fold-no-app-bar` | The fold on an event page ×3 | 10.9 s | ~27 s | H1 |
| `notice-while-busy` | A card during a hide ×3 | 11.4 s | ~29 s | H4 |
| `notice-once` | One notice for each card; expected values only in the log | 12.1 s | ~25 s | N2 |
| `badge-peek` | Look of the spin and the peek | 12.6 s | ~25 s | – |
| `deck-throw-tuck` | Look of the throw and the tuck ×3 | 7.3 s | ~22 s | S8 |
| `page-moves` | Look of tab, sheet, push and back ×2 | 12.3 s | ~30 s | – |

### 1c. Stress, demo and tools

| Mode | What it proves | Time | Issue |
|---|---|---|---|
| `--demo --stress` (8 cycles) | Empty frames over tabs, sheet, push, back | ~55 s (window messages, estimate) | I1, I2 |
| `--stress --sheets` / `sheets.sh` | Empty frames at the sheet open | 40 s tray; ~75 s each `sheets.sh` run | I1, I2 |
| `--stress --opens` | Empty frames at the end of an open | 28.8 s tray | – |
| `--demo` | Full flow for a video. The only run that clicks minimize, close and the back link | 34–35 s | I8 |
| recorder `mp4` | GIF → MP4 | ~6 s each | – |

### 1d. Unit tests that cover the tray or the motion

| Group | Covers | Covers in part |
|---|---|---|
| `TrayFlowTests` (4) | Window order of the open; a page error is a fault (H3); notice during a hide (H4) | `notice-while-busy`, `exceptions` |
| `TrayNoticeTests` (3) | The N2 notice rule | `notice-once` (not the window wiring) |
| `MotionTests` (~16) | Page depth → tab, push or back; presence leave | `page-move-in-panel`, `sheet-close` |
| `MotionRegistryTests` (5) | Registry matches `motion.css`; no timers; JS names exist | `steps-traced` (static part) |
| `MotionArchitectureTests` (4) | All times are tokens (I11); reduced motion = 1 ms (I13) | `reduced-flow-time` (CSS part) |
| `MotionReviewTests` (4) | JS error throws a fault; throw targets the card of the act | `deck-throw`, `exceptions` |
| `JsPipelineTests` (7) | Wait with fake JS; limit trace line; newer call cancels older | `no-wait-at-limit` |
| `WebTests` (5+) | Each view answers | `start` |

## 2. Overlap groups

| Group | Members | Same proof? | Keep |
|---|---|---|---|
| A. Dock and open | Checks 23, 24, 26, 29, 31, 33–35; `dock-open`; `fold-no-app-bar`; `--opens`; demo | No: each check has a different start state. `dock-open` logs the same value as `badge-in-ended`; its only extra is the video. `open-from-badge` and `badge-input` both prove "open from the badge" | The checks. Merge 26 into 31. `dock-open` only when the fold or grow look changes |
| B. Sheet | Checks 5–8; `late-press`; `keys`; `--sheets`; stress; demo | `keys` is replaced by checks 6 and 7. `late-press` proves a race that no check covers | Checks 5–8. `late-press` on request. Remove `keys` |
| C. Tabs and page moves | Checks 11–22; `page-moves`; `status-bubble` | The tab loop and the dot loop use the same 5 tabs 2 times, but measure different things | Merge the dot loop into the tab loop. `page-moves` only when a page move look changes. Replace `status-bubble` with a setting check |
| D. Notice and badge | Checks 28, 34; `notice-while-busy`; `notice-once`; `badge-peek`; unit tests | Check 34 and its scenario test the same H4 path. `notice-once` adds window wiring but has no pass rule | Checks 28, 34 and the unit tests. Make `notice-once` a self-test check. Other recordings only when the look changes |
| E. Deck | Check 32; `deck-throw-tuck` | The scenario is the video only | Check 32. Scenario only when the deck look changes |
| F. Reduced | `reduced-flow-time`; `dock-open-reduced`; unit test | A reduced recording shows only jumps | The check, before a merge. Remove `dock-open-reduced` |
| G. Label | `label` in the self-test and each scenario | Same function, cost 0 | Keep both |
| H. Trace totals | Checks 36–39 | Unique; they need all flows in one process | Keep all |
| I. Real pointer | `--mouse`; demo `--pointer` | `--mouse` has pass rules | `--mouse` on request |
| J. Start cost | All scenarios, stress, demo | Each run starts .NET, WebView2, the slot and the recorder: ~6–17 s, plus ~6 s for the MP4 | One run for a list of scenarios |

## 3. Recommendations

| Test | Action | Saved |
|---|---|---|
| Checks 1–9, 11–16, 23–25, 27–39 | Keep | – |
| `push-data` | Keep, but show it as data, not as a check | 0 |
| `dot-*` (6) | Merge into the tab loop | ~4 s per run |
| `open-from-badge` | Merge into `badge-input` | ~2.5 s per run |
| `notice` | Keep. Optional: a short peek time token in the self-test (then the real 5 s is not measured) | ~5 s (optional) |
| `badge-hover-fits` | Keep, on a known-failure list with I7, so the run can be green and a new failure shows | 0 |
| `--mouse` | On request only (moves the pointer) | – |
| `--reduced` self-test | Only before a merge, or when a step changes tokens or waits | ~38 s per step |
| `status-bubble` | Remove the recording; add a check that `IsStatusBarEnabled` is false on both windows | ~20 s |
| `keys` | Remove | – |
| `press-rules`, `late-press` | On request only, when `Press` changes | – |
| `notice-once` | Make it a self-test check with a pass rule; remove the recording | +8 s self-test, −25 s recording |
| `dock-open-reduced` | Remove | ~20 s |
| Look recordings (`dock-open`, `page-moves`, `deck-throw-tuck`, `badge-peek`, `fold-no-app-bar`, `notice-while-busy`) | Only when the step changes the look of that motion. Keep one approved baseline MP4 of each | 25–100 s per step |
| `--stress`, `--sheets`, `--opens`, `--demo` | On request only | – |
| New: `--scenario a,b,c` (one recorder, one MP4) | Proposal | ~15 s per extra scenario |

Totals: full self-test ~45 s → ~38 s. Refactor step 70–145 s → ~38 s (no look change) or ~65 s (one recording).

## 4. Proposed rule for proofs

| When | Runs |
|---|---|
| Small change, no motion or window code | The unit tests of the group (`--filter Motion`, `--filter Tray`). No tray run. |
| Small change to tray, CSS or JS | The unit tests. One self-test. No recording. |
| One refactor step | Full unit tests. One self-test. If the step changes the look of one motion: record only that scenario and compare with its baseline. A flaky check: at most 3 tries, then stop and report. |
| Before a merge | Full unit tests. The self-test. The `--reduced` self-test. One batch recording of the scenarios whose motion changed; the new MP4s become the baselines. On request only: `--mouse`, the demo, the stress modes. |

The user approves a look one time for each motion and each merge, not one time for each step.

## 5. Gaps

| Gap | Effect |
|---|---|
| Minimize, close and the back link are clicked only in the demo and stress runs, with no pass rule | A broken button passes the self-test, because the self-test calls `w.Request(...)` |
| The tray icon click | Not tested |
| Motion path and duration (`motion-times`, issue #60) | A wrong curve or duration passes |
| The gap between the windows (I15) | Listed, no pass rule |
| Badge shadow cut (I18), grow look (I6) | Only videos show them |
| Empty-frame detector (I2) | Stress counts not reliable; stress never fails on empty frames |
| Keyboard | Only typing and one Tab. No Escape, no Enter |
| `--live` mode | The self-test uses mock data only |
| DPI change, second screen, slot lock, `SingleInstance`, `RunRecord` | No unit test |
| Known failure `badge-hover-fits` (I7) | Each run ends with exit 1, so the exit code cannot gate a step |

## R2 additions (2026-11-21)

| Check | Kind | What it proves | Issue |
|---|---|---|---|
| `badge-box-fits` | self-test | The shadow boxes of the tab and the bubble, and the ring, are inside the click area at rest, hover and peek. It replaces `badge-hover-fits` | I7, I18 |
| `badge-fold-area` | self-test | No cut in any sample of the hover fold; the click area gets small after the fold | I7 |
| `panel-box-fits` | self-test | The shadow of the open panel is inside its window | I10 |
| `Tray_sizes_are_tokens` | unit | `tray.css` lays out the window sizes with tokens | I10 |
| `Tray_window_sizes_come_from_the_page` | unit | The tray windows keep no size constant, except the edge gap | I10 |
| `badge-states` | scenario | The badge at rest, hover and peek, with the boxes in the log | I7, I18 |

The known failure `badge-hover-fits` is gone: the self-test exit code can gate a step.
