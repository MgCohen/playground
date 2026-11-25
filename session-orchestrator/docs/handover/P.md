# Handover: task P, the next pages of the UI

Date: 2026-11-25. Goal of the task: plan and sketch the advanced features (F1 to F15) of the orchestrator UI. Drafts only, no app code.

## Where it is

| Item | Value |
|---|---|
| Branch | `orch/session-1ea8d8e0` |
| PR | https://github.com/MgCohen/playground/pull/56, open, base `orch/c5-c3-findings` |
| Artifact: Advanced Features | https://claude.ai/artifact/Kn888Us7Y4A7MnPTiQNmUj |
| Artifact source | `docs/review-pages-spec.html`, with supporting files in `docs/` (`run-views.html`, `run-views/`, `workflows-sketches.html`, `workflow-graphs.html`) |
| Model source for F2 | https://claude.ai/artifact/4hexcdNxAtjP4ZF1hoonC8 (Workflow Shape Options, P5) |
| Store design issue | https://github.com/MgCohen/playground/issues/80, open |
| Decisions | `docs/decisions.md`, rows "P" |
| Page intents | `docs/f2-page-intents.md`, `docs/f3-page-intents.md` |
| Plan section | `docs/plan.md`, section of task P |

## State

| Feature | State |
|---|---|
| F2 Workflows | Sketched, accepted as the starting ground. Round 5 is in `docs/run-views/r5/` |
| F3 PR stack | Views confirmed. One sketch round is done (`docs/run-views/f3/`, nine sketches). It waits for Q-S6 and Q-S7 |
| F4 PR | Spec only. F4 is the full form of the PR template. `f3/prev-c.svg` is its first sketch |
| F13, F1 | Not sketched. They come after F4 |
| F5 to F12, F14, F15 | Backlog in the tracker |

The done-when check: the plan is approved, the sketch page is published and in `docs/`, the gaps are recorded, and PR 56 is open on its base. The gaps and the PR are complete. The sketch work is not complete. The tracker shows F3 as "spec" until Q-S7 has an answer. No `done` report was sent.

Two orch queue cards are stale: Q-R9 (round 5 pick) and Q-S1 (F3 intent). The user answered both in chat. Do not ask them again.

## Open questions, oldest first

The user answers one queue, oldest first. The user works in the root chat only. Write each question in full in the reply; the root shows it. A new question goes to the end.

Q1 has an answer: task PRs and decision PRs both show. Q4 has a partial answer: the lowest PR comes first, and approve also merges for now. "Merge any ready PR" and "merge all ready" have no answer. Q7 is not in the list. Q12 has an answer in `decisions.md`.

**Q2. Do I sketch a full stream page too?**
- Context: the brief names three pages, and no stream page exists. The place of all views is decided at the unification of the features.
- Options: (a) only the stream bar on each page; (b) the bar plus one small stream page.
- Recommendation: (a).

**Q3. How does the run page measure progress?**
- Context: loops make "3 of 6" vague.
- Options: (a) visited steps of the total; (b) done steps on the shortest path to a done step, plus a loop count.
- Recommendation: (b). It stays correct after a loop.

**Q5. Which frame do the sketches use?**
- Context: the drafts use the 350 px widget frame. The F2 and F3 sketches use a 360 x 720 phone frame.
- Options: (a) widget only; (b) widget plus one wide variant for each page.
- Recommendation: (b). A diff and a stack are easier to read on a wide page.

**Q6. Is PR review a workflow definition?**
- Context: the rules of F3 and F4 fit the step format of C2. As a workflow, the PR page becomes a run page with a PR view.
- Options: (a) no, keep the review rules in `orch`; (b) yes, sketch the pages for it and record it as a roadmap item.
- Recommendation: (b), as a record only.

**Q8. What is the unit inside a project, for a big feature?**
- Context: a project is a repository. A big feature has many sessions and PRs. The C1 stream already groups many sessions.
- Options: (a) the stream: project → stream → task; (b) a new unit, for example "initiative"; (c) the stream stays free, and a project is only a filter.
- Recommendation: (a). It adds no new term.

**Q9. Can one stream span more than one project?**
- Context: F8 puts the sessions of one feature in different repositories.
- Options: (a) yes: one main project, and sessions of other projects; (b) no: one stream for each project, and a link between the streams.
- Recommendation: (a). The work is one piece.

**Q10. Does the session status page (F13) join the first set?**
- Context: the run page and the PR page link to the session page. That page does not show runs, PRs or the registry.
- Options: (a) yes, sketch it with F2 to F4; (b) no, backlog.
- Recommendation: (a).

**Q11. Where does the GitHub data of F14 come from?**
- Context: issues and CI status are only on GitHub. The design rule says that the store is the source.
- Options: (a) a live read with `gh` and a short cache; (b) the supervisor copies the state into envelopes on a timer; (c) webhooks through the tunnel (D1).
- Recommendation: (a) first. It needs no new store kind.

**Q13. What does the change log (F15) show?**
- Options: (a) the finished changes (merges, applied decisions, workflow versions, app releases), for each project and stream; (b) only the release notes of the app; (c) a feed of all events.
- Recommendation: (a).

**Q-R6. Does the control act "go back to step X" stay with P5?**
- Context: decision B2 added it before P5. In P5, a restart starts a new pass of the current flow.
- Options: (a) keep it; (b) drop it.
- Recommendation: (b). The user can restart the current flow, with the reason change.

**Q-P1. Which split kind does the first build support?**
- Context: P5 has choice and parallel. The first build supports one kind.
- Options: (a) choice; (b) parallel.
- Recommendation: (a).

**Q-P2. Can a step keep its last result on a restart, so that the new pass does not run it again?**
- Context: a restart runs the flow again from step 1.
- Options: (a) yes, as an option of the step, off by default; (b) no.
- Recommendation: (a).

**Q-P3. How do old passes fold?**
- Context: each restart adds a pass to the run page.
- Options: (a) one row for each old pass, and one row "N passes" for a flow with many passes; (b) no fold.
- Recommendation: (a).

**Q-S6. Is "comment" the same act as "change"?**
- Context: the spec has approve, change and refuse. The user said view, comment, reject and pre-approve. The sketches use Approve, Comment and Reject. A change blocks the merge until the session updates the PR.
- Options: (1) the same act, each comment blocks the merge; (2) two acts; (3) one Comment act with a switch "ask for a change".
- Recommendation: (3). It keeps three buttons, and a simple remark does not block the merge.

**Q-S7. Are the F3 sketches a sufficient starting ground, and which variation for S-1 and S-2?**
- Context: F2 ended as "sketched", with the details in the build.
- Options: (1) yes, the Tree and Cards switch with one card at a time (S-1 a and b, S-2 a and c); (2) yes, tree and cards together (S-1 c); (3) yes, the switch with the list of cards (S-2 b); (4) no, another round.
- Recommendation: (1). It supports approval from the bottom, one PR at a time.

## Open items that are not questions yet

| Id | Item |
|---|---|
| O-1 | Conditions: where they live, and how a path selects a way out |
| O-2 | The PR template: its fields and its actions |
| O-3 | The place of the F3 views in the UI |
| O-4 | The settings for approve and merge |
| W-C1 | The step bar: only if it can show hubs, restarts and levels |
| Idea | A page of all open PRs of a project |

## Answers and preferences of the user

The answers are in `docs/decisions.md`. These preferences are not there:

- Go feature by feature: confirm the intent of each view, then one sketch round with variations.
- F3 is a feature, not a page. Show the feature name, the intent, the list of views and the open questions separately.
- Keep each view simple and clean. Phone first.
- The V2 work decided the UI base (D4: `Orchestrator.View` and `Orchestrator.Ui`; D5: the type selects the renderer). Keep the sketches independent of it.

## Rules

- Never use or touch the live store or port 7777. For a test server, use ports 7820 to 7829, a copy of the store, a fake claude and `ORCH_IMPORT=0`.
- Never use click questions. Merge only with `orch merge`.
- If `decisions.md` or `plan.md` has an append conflict with the base, keep both sides, the base rows first.

## Next steps

1. Get the answers to Q-S6 and Q-S7. Record them in `decisions.md` and `f3-page-intents.md`.
2. If Q-S7 is yes, set F3 to "sketched" in the Overview table of `review-pages-spec.html`, and publish.
3. Start F4 with the intent pass and the PR template (O-2). Then F13, then F1.
4. Report done when the done-when check passes.

## Test and publish

1. In `docs/`, run `python -m http.server 7825 --bind 127.0.0.1`. Open `/review-pages-spec.html#stack/sketches`. Locally, "·" shows incorrectly: the file has no charset line, and the artifact adds it.
2. To make the sketches again, run `python gen.py` in `docs/run-views/tools/f3`, `r5a` or `r5b`. The output goes to `docs/run-views/f3` or `r5`.
3. Read the artifact with its URL. Read each supporting file that you replace, with `path`. Compare it with the last commit; the published text files can have CRLF line ends.
4. Publish `docs/review-pages-spec.html` with `url`, `root` = `docs` and the changed files in `files`.
5. For a render check, run `msedge --headless=new --window-size=360,720 --screenshot=<png> <svg>`.

## Only on the old disk

- The generators of sketch rounds 1 to 4, the Edge profiles and the PNG files (session scratchpad). Their SVG results are in `docs/run-views/`. They are not necessary.
- The orch queue cards and the session record (store).
