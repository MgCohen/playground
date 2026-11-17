# Decider plan

Status: decided, except the items in "Open". The owner decided A1 to G4 on 2026-11-17, and B1 to B9 in the briefing of the engine work on the same day.

Visual map of the decider today (anatomy, flow, input and output, domains, real run): [Decider Map](https://claude.ai/artifact/QmgUexiTLK3c51eT1EzFE3).

## The problem

The run `hand-rule-001` failed. The root selected `cqrs_system` with the children `query_handler` (Request→View) and `store` (Event→Saved). No child gives an Event. The expected solution is `command_pipeline` → `store`, with `io` Request→Saved.

| Cause | Where |
|---|---|
| One call does select, status and split (rules 1–8 of `agents/decider.md`) | `DecideStep.RunAsync` |
| The split loses the context of the request | rules 7–8, child text |
| Child calls run in parallel and do not know about each other | `Solver` level queue |
| No code checks the selected set by its ports before compose | `Solver`, `JoinStep` |

## Decided

| # | Decision | Reason |
|---|---|---|
| D1 | The decider is the catalog picker. Its only job is select: it gets the requirements, the context and the catalog, and returns the entry that matches most closely and how well it fits. | Owner: "the decider is just pretty much the catalog picker". One job per role. |
| D2 | The split (rules 7 and 8: children and open) leaves the decider. | Follows from D1. The split caused the context loss in `hand-rule-001`. |
| D3 | A step before the decider makes the requirements as a spec. The decider picks against the spec, never against a fragment of the request text. | Owner: "that gives us exactly what we want. It's a spec". |
| D4 | The spec uses the anchor form: a small context, then short binary rubrics. The count of rubrics depends on the module. | Owner: the anchor is the general format for any check or comparison. |
| D5 | The decider returns the result of each rubric and a score (0 to the count of rubrics), not only a status. | Owner: later steps must compare many partial or full options, and pass is not always 100%. |
| D6 | What full, partial and pass mean is a setting, not a fixed rule. | Owner: "we can configure later what does partial or what does passing mean". |
| D7 | All limits are configurable. | Owner: "Any limit should always be configurable." |
| D8 | Decider and grader are two roles. The grader gets one element, the target rubrics, how to evaluate, and minimal context, and returns the result of each rubric and the score. The decider filters the options (mostly code), sends a grade request for each option that needs one, analyzes the graded options and picks. To compose, it takes the best option, makes the failed rubrics the remainder, and repeats. | Owner's model, sent from the main session. The grader has the smallest context, and the same grader grades the solution after the run. |
| D9 | A new test domain, an RPG party, tests composition without ports. Another session plans it. | Owner, from the main session. |
| A1 | Anchor field names are final. Front matter: `id`, `kind` (intent, constraints, success, review), `level` (engine, pack, case), `score` (`rule`: all or percent, `pass_at`), the body text, and `domain: {...}` that the pack owns. Rubric: `id`, `ask`, `check` (grader or code), `args`, `visibility`. | Owner, 2026-11-17. Closes O9. |
| A2 | `visibility` is `run` or `eval`. Eval rubrics never go to the run. | Owner. Closes O10. |
| R1 | Code checks during the run are a setting of each pack (`compose.code_checks`). Party: off. Topology: on, with `check: code` rubrics for port types and a set check after each pick (the ports connect, each loop has an entry). | Owner. Closes O3 and O7. |
| R2 | One spec role for all domains: `decomposer`. Each pack adds its own rules file. A missing value goes to the ask step. | Owner. Closes O4 and O12. |
| R3 | The loop stops when the remainder is empty, when the remainder did not become smaller in one turn, or when the turn count reaches the limit in the settings. | Owner. Closes O5 and O6. |
| G1 | Verdict cache key: element hash + rubric hash + checker version + context hash. | Owner. Closes O8. |
| G2 | Each pack keeps grader fixtures with known answers. Run them when the model or a grader file changes. | Owner. |
| G3 | The report has one line for each anchor: score, failed rubrics, measures. No weighted total. | Owner. |
| G4 | Remove the setting `grading.information`. | Owner. |
| B1 | Only a pack with `engine.decider: compose` uses the compose loop in the engine (topology). Shapes and circuits keep the status decider (`agents/decider.md`) until a later change. | Owner, briefing. Closes O14 scope. |
| B2 | A catalog entry of the engine is one pattern variant, for example `command_pipeline.validated`. A required param goes to the ask step. | Owner, briefing. |
| B3 | A group pattern (`cqrs_system`) is not an entry. If a committed set fits the parts of a group, code wraps the set in that group. | Owner, briefing. |
| B4 | The pack setting `compose.gaps` lets a commit keep a remainder with `gap: {new_id, nearest}`. The author step writes the new pattern from the remainder rubrics. Topology: on. Party: off. | Owner, briefing. |
| B5 | The decider agent picks. When a stop of R3 fires, code gives the decider one last turn, and that turn must commit. | Owner, briefing. Closes O1. |
| B6 | The party control mode `spec: given` writes a copy of the hidden rubrics with `visibility: run`, and the trace records it. | Owner, briefing. |
| B7 | The decomposer schema is generic: `needs: [{kind, name, amount}]`, `missing`, `note`. The pack code makes the rubrics. | Owner, briefing. |
| B8 | A stamp file keeps the model and the grader file hashes of the last fixture run. `pe eval` warns when the stamp is old. `pe grader-check <pack>` runs the fixtures. | Owner, briefing. |
| B9 | The grade report keeps `consistent` as a measure. | Owner, briefing. |
| V1 | A verdict is true or false. A rubric with no verdict counts as false, so it is in the remainder. | Composition plan C8. Closes O2. |

## Open

| # | Item | Proposal |
|---|---|---|
| O11 | Search for cost and budget | "Pick the best, then repeat" is greedy. Proposal: the grader gives the verdicts, and code does the search. The greedy loop is the baseline. |
| O13 | Trace changes | For each node: the spec, the verdict matrix (entry × rubric), the score, the pick, the remainder, the set checks, the cache hits. The compose trace has most of these. |
