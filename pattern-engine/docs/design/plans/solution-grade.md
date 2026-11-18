# Solution grade: plan

Status: decided, except the items in "Open". The owner decided A1 to G4 on 2026-11-17. The codes are the same in [decider.md](decider.md).

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
3. The engine computes `consistent` as a measure. It is not a pass condition.
4. `ComposeStep` calls `StructureGrader.Check` inside the compose loop.

## Decided

| Item | Decision | Reason |
|---|---|---|
| Code or agent | Code decides each check that code can decide. An agent makes only judgments. | Code is deterministic, cheap and testable. |
| Modular parts | Agent files, rubrics and templates are files. Settings select them. | A domain changes files, not code. |
| The pass | The output decides the pass (Q17). The solution grade does not change the pass. | Already decided in D26 and D27. |
| Test runs | Small Haiku runs only (`pe eval <domain> --mode dev --k 1`). Full runs only if the owner asks. | Cost. |
| Anchor format | The ICS format (Intent, Constraints, Success) is a format that the owner likes. It is a reference, not a decision about the unit of the grader. | The owner must first understand what the grader is. |
| Definition (item 1) | Three inputs: what was asked, what was made, the standards. Three outputs: correct, a score, the failed checks. | G3 |
| Unit (item 2) | One grade request: an element, its rubrics and minimal context. One verdict for each rubric. | G1 |
| Named layer (item 3) | The anchor. | G3 |
| Anchor format (item 4) | Front matter `id`, `kind`, `level`, `score` (`rule`, `pass_at`), body text, `domain`. Rubric `id`, `ask`, `check`, `args`, `visibility`. | A1 |
| Checker routing (item 5) | Code checks first (`check: code`), when the pack turns them on. The grader agent gets only `check: grader` rubrics. | R1 |
| Score (items 6, 14) | One line for each anchor: score, failed rubrics, measures. No weighted total. | G3 |
| Measures (item 7) | A number beside the verdicts, not a rubric. | G3 |
| Visibility (item 8) | `run` or `eval`. Eval rubrics never go to the run. | A2 |
| Trust of a model checker (item 9) | Fixtures with known answers for each pack. Run them when the model or a grader file changes. | G2 |
| Use during a run (item 11) | The decider sends grade requests during the run. | R1, R3 |
| Cache key (item 12) | Element hash + rubric hash + checker version + context hash. | G1 |
| `grading.information` (item 15) | Removed. | G4 |

## Open

| # | Question | Recommendation |
|---|---|---|
| 10 | Grader trust for selection, questions and proposed. | Add fixtures and mutations for each grader that the solution grade uses. |
| 13 | Field names at the pattern level: `covers` and `excludes`, or I, C and S on the pattern. | Decide with the decider plan. |
