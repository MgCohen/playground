# W: handover of the event stream audit

The audit is complete as a proposal. The approval is deferred. No app code changed, and no fix task started.

## Goal and rules

Goal: check that each feature reads from and writes to the event stream, list the bypasses, and give a fix plan for each.

Rules from the user:

- Change no app code without a request. Never touch the live store or port 7777. No merges.
- K owns the architecture. N owns the event store research. Share findings with them through the root.
- The user works only in the root session. Send questions with `ask_user` and links with `report` or `update`. This replaced the earlier rule "only in your own chat, no `ask_user`, no `update`".
- **Approval: keep the question open, apply no default, start no fix task, and do not ask again.** The user reads the audit later together with K.

## Files and links

| Item | Place |
|---|---|
| Page (rules, audit, fix plan) | `docs/stream-audit.html`, https://claude.ai/artifact/DKqHidbSJD1JTPHeKCKf7t |
| Record and task table | `docs/plan.md`: "W result: stream audit" and "Stream audit fix tasks (W)" |
| Full feature tables and raw findings | `docs/handover/W-raw-reports.md` |

The page is private to its owner. It had no comments on 2026-11-25.

## State

| Part | State |
|---|---|
| Rules R1 to R8 | Written. They use the defaults of Q1 to Q7 |
| Audit: 51 findings, 14 groups (W-1 to W-14) | Done, on `c445988` |
| Fix plan: W1 to W12 | Done as a proposal |
| Review of the seven default PRs | Not done |
| Approval by the user | Deferred |
| Notes to K and N | Not sent |
| Fix tasks | Not started |
| Final `done` report | Not sent |

`orch/d-data-upgrade` moved after the audit: it was at `f8b3f8a` on 2026-11-25. Check the file and line references against the current code before a fix.

## Trust level of the audit

Three agents read the code. Only a part was checked by a second read:

- Checked in the code: the writes of `status.json` (W-1) and `lease.json` in the plugin, no poster of `lease.take` (W-2), the registry writes of stop, resume, archive and new session (W-4), the supervisor memory (W-1, W-11), the LiveView and tick timers (W-13), no caller of `DefaultsToReview` (W-14), the sampled lines of `Supervisor.cs`.
- Not verified: how complete the stack envelopes are (W-9), the rest of the line numbers, whether `session.json` counts as a projection.
- No test ran. The fixes are designs only.

Corrections of 2026-11-25: the finding count is 51 (the page said 46); the hook line numbers; the Cards page and the Stream page are not fully clean, because both call `Board.Tree`.

## Open questions

**Approval (id `toolu_01TXLCoA9HSoobdHKXZ7dQxc`, risk medium, no default, deferred).** Do you approve the stream audit and the fix plan W1 to W12?

- Context: messages go through the stream; the session lifecycle lives in side files and memory. 14 groups, 12 tasks, facts first and views last.
- Options: approve; approve with changes (name them); refuse (give the reason).
- Recommendation: approve, then send the notes to K and N.

## Answers: all are policy defaults

The user answered none of Q1 to Q7. The time limit applied each default. The user reviews each one in its draft PR. The context, all options and the reasons are on the page (tabs Open questions and Fix plan).

| Question | Default | Decision id |
|---|---|---|
| Q1 Strictness for session state | (c) Mixed: lifecycle facts are envelopes, plumbing in files | `toolu_01MdH4QCVyayX8y4XxETmTQM` |
| Q2 Poll timer is a bypass | (a) Yes | `toolu_01XusYnnZZqAn3rNrTTYTxnY` |
| Q3 Time rules as events | (b) Sweep allowed: reads projections, writes envelopes | `toolu_01EwbPveBqaPdPiaxJSHVU7S` |
| Q4 Depth | (b) Also CLI, tray, providers | `toolu_019Zy1Nuc9AfT4Mx3kixRBi8` |
| Q5 Known bypass first | (b) Start from the code | `toolu_016UV5GVjrQ1yTc1HutZjJZv` |
| Q6 Config as a domain fact | (b) Outside the audit, note for K | `toolu_01Hp8YqKr6tA6PxMKtY3uCNk` |
| Q7 Liveness | (b) `heartbeat.txt` stays; the sweep posts `session.stale`, `session.fresh` | `toolu_01X3Vy3JUZWV7o9Cod3TAenY` |

One conflict for the review: R8 says that a sweep reads only projections, but Q7 (b) lets the sweep read `heartbeat.txt`. If the user accepts Q7 (b), R8 needs this exception.

## Branches and draft PRs, from the lowest

Each branch is on top of the one before it. All PRs are open drafts. Merge nothing.

| # | Branch | PR | Content |
|---|---|---|---|
| 0 | `orch/session-480339a0` (from `orch/d-data-upgrade` at `c445988`) | None | First version of the page |
| 1 | `orch/decision-toolu_01XusYnnZZqAn3rNrTTYTxnY` | #84 | Q2 |
| 2 | `orch/decision-toolu_019Zy1Nuc9AfT4Mx3kixRBi8` | #86 | Q4 |
| 3 | `orch/decision-toolu_016UV5GVjrQ1yTc1HutZjJZv` | #87 | Q5 |
| 4 | `orch/decision-toolu_01MdH4QCVyayX8y4XxETmTQM` | #89 | Q1 |
| 5 | `orch/decision-toolu_01EwbPveBqaPdPiaxJSHVU7S` | #91 | Q3 |
| 6 | `orch/decision-toolu_01Hp8YqKr6tA6PxMKtY3uCNk` | #93 | Q6, and the audit commit `e4fbbd4` |
| 7 | `orch/decision-toolu_01X3Vy3JUZWV7o9Cod3TAenY` | #102 | Q7, the deferred status, this handover |

The audit and this handover are in decision PRs (#93 and #102), not in PRs of their own. If the user refuses Q6 or Q7, keep these commits with `git cherry-pick`.

## Next steps

1. Check out branch 7. Read this file, then the page.
2. Wait for the answer of the user about the audit. Do not ask it again.
3. If the user reviews a default PR, follow the skill `orch:orch-stack`. The store with the stack records stays on the old computer, so `orch decision` and `orch stack` can fail. Then use `git` and `gh` with the table above.
4. After the approval, send the two notes through the root, then report `done`:
   - K: projections need one home and one owner (R7). Config stays outside the stream (Q6).
   - N: W1 to W9 add lifecycle envelopes. The store needs fast folds by session and a push for each append.
5. Start a fix task only if the user asks.

To change the page from another session, read the artifact by its URL first, then publish with that `url`.
