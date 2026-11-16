---
name: pattern-nofit
description: Handle a request that no pattern fits. Find the nearest pattern, list the changes, make a proposed pattern file and plan.json. Use it from pattern-navigate.
---

# No pattern fits

Do not force a pattern that does not fit.

1. **Nearest.** From `node pe.mjs index`, select the pattern that is not a leaf and has the most parts that the request needs.
2. **Deltas.** List each change from the nearest pattern: parts to add, remove or change. Use known leaf parts.
3. **Scaffold.** Give the new thing a short id, for example `lighthouse`. Run `node pe.mjs scaffold <id> --from <nearest>`.
4. Edit the new file `patterns/<id>.md`: `use_when`, `parts`, the Intent section. Keep `status: proposed`.
5. Write `plan.json` with `<id>` as the top pattern.
6. Write `steps/nofit.json` with the Write tool. Do not print it as a message: `{ "outcome": "no-fit", "nearest": "<id>", "deltas": [ ... ], "proposed": "patterns/<id>.md" }`.

## Then

This skill is one step. Do not stop here. Go back to the skill that called it and continue with its next step.
