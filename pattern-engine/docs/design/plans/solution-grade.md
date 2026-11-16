# Solution grade: plan

Status: not final. The planning session closed before the owner decided the open items. This file keeps the state of the discussion.

References:

- Grader Map page (context, outside view, inside view, current graders, structure, anchor proposal): https://claude.ai/artifact/9hmmqoyg57W9fH8unVdhsi
- Anchor format and grader interface draft: `solution-grade-anchor-draft.md`, in the shared notes folder of the planning sessions (outside this repo).

## Current state

| Item | Today |
|---|---|
| Grader | A C# `IGrader` class. It returns `Verdict(Ok, Failures, Skipped)`. |
| Pass | `grading.output`: all verdicts ok (Q17, D26). |
| Solution score | `grading.solution`: the fraction of the solution graders that ran and passed (D27). The list is empty in `pe.settings.yaml`. |
| References | Catalog and subject rules (structure, rules). Expected output of the case (behavior, flow, questions). Expected tree (selection, built). |
| Grader trust | Fixtures, references and mutations for structure, rules, built, behavior and flow. |

Gaps found:

1. The selection and built graders need an expected tree. An expected tree holds for one solution only.
2. Grader trust does not cover selection, questions or proposed.
3. No code reads the setting `grading.information: [consistent]`. The engine always computes `consistent`.
4. `ComposeStep` calls `StructureGrader.Check` inside the compose loop.

## Decided

| Item | Decision | Reason |
|---|---|---|
| Code or agent | Code decides each check that code can decide. An agent makes only judgments. | Code is deterministic, cheap and testable. |
| Modular parts | Agent files, rubrics and templates are files. Settings select them. | A domain changes files, not code. |
| The pass | The output decides the pass (Q17). The solution grade does not change the pass. | Already decided in D26 and D27. |
| Test runs | Small Haiku runs only (`pe eval <domain> --mode dev --k 1`). Full runs only if the owner asks. | Cost. |
| Anchor format | The ICS format (Intent, Constraints, Success) is a format that the owner likes. It is a reference, not a decision about the unit of the grader. | The owner must first understand what the grader is. |

## Open

Each item has the recommendation of the planning session.

| # | Question | Recommendation |
|---|---|---|
| 1 | Is this definition of the grader correct? Three inputs: what was asked, what was made, the standards. Three outputs: correct (yes or no), how good (a score), why (the failed checks). | Yes. One grader serves the decider, the run, the eval and the report. |
| 2 | What is the unit of the grader: one rubric, one anchor, or one grade request? | One grade request: an element plus its rubrics plus minimal context. The answer is one verdict for each rubric. |
| 3 | Which layer gets a name, a setting, a trust test and a line in the report: check (rubric), group (anchor), or grader? | The anchor. A rubric is too small, and a grader is too large. |
| 4 | The anchor file format (front matter `id`, `kind`, `level`, `source`, `score`; rubrics with `id`, `ask`, `check`, `args`, `visibility`). | Use the draft. Agree it with the decider session. |
| 5 | Checker routing: code first, then a decision model, then a judge. | Yes. The grader returns verdicts only. Code applies the score rule of the anchor. |
| 6 | One weighted score, or one score for each anchor? | One score for each review anchor, shown apart. No weighted number yet. |
| 7 | Measures (cost, size, agent calls, reuse, coverage, coupling). | A measure is a number beside the verdicts, not a rubric. A rubric can set a limit on a measure. If code knows the best value, the report shows it: "cost 12, best 10". |
| 8 | Rubric visibility: `run` (the decider sees it) or `eval` (only the grade after the run). | Keep both values. Eval rubrics test if the decider meets standards that it did not see. |
| 9 | How does grader trust test a model or judge checker? | Calibration cases with known answers, for each checker. Define them before the first judge check. |
| 10 | Grader trust for selection, questions and proposed. | Add fixtures and mutations for each grader that the solution grade uses. |
| 11 | When does the engine use the grade during a run? | Later. First use the grade after the run only. Then let the decider send grade requests. |
| 12 | Cache key for verdicts. | Element hash, rubric id, checker version. |
| 13 | Field names at the pattern level: `covers` and `excludes`, or I, C and S on the pattern. | Decide with the decider session. |
| 14 | Settings, report and metrics for the solution grade. | One line for each anchor in the report: score, failed rubrics, measures. Settings select the anchors and the score rules. |
| 15 | The setting `grading.information`. | Read it in code, or remove it. |
