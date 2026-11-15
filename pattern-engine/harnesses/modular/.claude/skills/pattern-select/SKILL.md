---
name: pattern-select
description: Select one pattern, its variant and its params for a request or for one part of a request. Use it from pattern-navigate and pattern-divide.
---

# Select a pattern

Input: a request text, and the allowed pattern ids (all, or the parts of a parent).

1. **Candidates.** From `node pe.mjs index`, take the allowed patterns whose `use_when` matches the text.
<!-- ablate:no-dont-use -->
2. **Exclude.** For each candidate, read its `do_not_use_when`. If a `when` matches the text, replace the candidate with its `use`. Do this check even when the words of the text name the candidate.
<!-- /ablate -->
<!-- ablate:no-nofit -->
3. If no candidate is left, or a `use` is `no-fit`, the result is `no-fit`.
<!-- /ablate -->
4. If two candidates are left, take the one with the more specific `use_when`. Record the reason.
5. **Variant.** Run `node pe.mjs parts <id>`. Take the variant whose `when` matches the text.
6. **Params.** Set a param only if the text names its value.

Write the result with the Write tool to `steps/select-<name>.json`. Use `top` as the name for the full request, and the part path for a part, for example `select-street.2.json`. Do not print the result as a message.

The file content:

```json
{ "p": "<id>", "v": "<variant>", "params": { }, "reason": "<one sentence>" }
```
<!-- ablate:no-nofit -->
or `{ "outcome": "no-fit", "reason": "<one sentence>" }`.
<!-- /ablate -->

## Then

This skill is one step. Do not stop here. Go back to the skill that called it and continue with its next step.
