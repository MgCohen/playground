---
name: pattern-navigate
description: Router for one pattern-engine request. Use it first for each request. It calls the step skills and the agents in a fixed order.
---

# Navigate a request

Do the steps in this order. Do not skip a step. Do not ask the user. Do not stop before step 8 is done: the request is complete only when the output file and `trace.json` exist.

| Step | Use | Result |
|---|---|---|
| 1 | Read `instructions.md`. Run `node pe.mjs index`. | The subject rules and the pattern index |
| 2 | Skill `pattern-select` on the full request. Then read `steps/select-top.json`. | The top pattern |
<!-- ablate:no-nofit -->
| 3 | If step 2 gives `no-fit`: skill `pattern-nofit`. Then go to step 5. | A proposed pattern and `plan.json` |
<!-- /ablate -->
| 4 | Skill `pattern-divide` on the top pattern | `plan.json`: the full pattern tree |
| 5 | Agent `composer`: "Build the output from plan.json." | The output file |
| 6 | Agent `verifier`: "Check the output." | A verify report (JSON) |
| 7 | If the report has failures: agent `composer` with the failures. Then step 6 again. Max 3 rounds. | A passing output |
| 8 | Run `node pe.mjs trace --write`. Then edit `trace.json` (see below). | `trace.json` |

## trace.json

`pe trace --write` writes `tree` from the output. Do not change `tree`. Set the other fields:

- `outcome`: `match`. `nearest`: `null`. `deltas`: `[]`. `proposed`: `null`.
<!-- ablate:no-nofit -->
- If step 3 ran: `outcome` is `no-fit`. Set `nearest`, `deltas` and `proposed` from step 3.
<!-- /ablate -->
- `questions`: each choice that was not clear, with the reason.
- `attempts`: the number of verifier rounds.

## Rules

- Each step skill writes its result to a file in `steps/`. Read the file, then start the next step in the same turn. Do not reply with a message between steps: a message with no tool call ends the run.

- Do not change `index.md`, `instructions.md`, or a pattern file that exists.
- If a step fails, write the failure in `questions` and continue with the next step.
