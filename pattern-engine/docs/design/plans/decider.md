# Decider plan

Status: planning, not final. The owner closed the planning session with open items. This file keeps the decisions and the open items for the next session.

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

## Open

Each item is a proposal from this session. The owner has not decided it.

| # | Item | Proposal |
|---|---|---|
| O1 | Who picks (question 3) | Code ranks and picks. The decider agent judges only a near tie. The margin is a setting. |
| O2 | Verdict values | `yes`, `no` or `unknown`, each with a short reason. An `unknown` goes to the ask step. The grader does not guess. |
| O3 | Code rubrics | A rubric with `check: code:<name>` (for example `code:io` against the pattern ports) gets its verdict from code. It never goes to the grader. |
| O4 | The spec step | A role `analyst` with its own agent file and schema. Input: the request and the anchors of the pack and engine. Output: the request spec, with typed `io` where the pack has types. Code checks that each type is in the catalog and that the requirements connect from input to output. Missing values go to the ask step. |
| O5 | Status and remainder | Code compares the score with the limits in `pe.settings.yaml`. The remainder is the rubrics with `no` for the picked entry. It becomes the spec of the next round. |
| O6 | Loop stop | Stop when the remainder does not get smaller, or at a max round count from the settings. |
| O7 | Set checks | After each pick, code checks the set: the ports connect, and each loop has an entry. A grade of one pattern cannot find the `hand-rule-001` failure. |
| O8 | Grade cache | Key: pattern id, pattern file hash, rubric hash, context hash. A change of one part makes a new grade. |
| O9 | Final anchor fields | Draft of the solution-grade session: one file per anchor; front matter `id`, `kind` (intent, constraints, success, review), `level` (engine, pack, case), `score` (all, percent, count, weights); 1–3 lines of context; rubrics with `id`, `ask`, `check`. Proposed additions: typed `args` for a code check; a `pattern` level (covers and excludes); a compiled `node` level with `source`; "yes" is always good; the decider reads Success too. The link from a rubric to the node that covers it goes in the trace, not in the anchor. |
| O10 | Eval independence | The eval also uses checks that the run never sees: hidden case rubrics, the expected solution, behavior tests. Otherwise the eval only confirms the run. |
| O11 | Search for cost and budget | "Pick the best, then repeat" is greedy. It does not always find the cheapest set within a budget, or share one pool across many specs. Proposal: the grader gives the verdicts, and code does the search. The greedy loop is the baseline. |
| O12 | Agent files and schemas | New `grader` agent file and schema (verdicts and reasons). New `decider` agent file and schema for the near-tie pick only. Replace `agents/schemas/decide.json`. |
| O13 | Trace changes | For each node: the spec, the verdict matrix (entry × rubric), the score, the status with the limits used, the pick and who made it (code or agent), the remainder, the set checks, the cache hits. |
| O14 | Tests and small runs | Unit tests for the code parts (score, status, remainder, loop stop, set checks, cache). Small runs only, on Haiku with `--k 1`: `pe eval topology --mode dev --only hand-rule-001 --k 1`, then `hand-partialfit-001`, then one RPG case when that domain exists. |
