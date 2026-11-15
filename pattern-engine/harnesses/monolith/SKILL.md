---
name: navigate
description: Select patterns from a pattern index, divide the request into parts, compose the output, verify it, and record a trace. Use for each request in a pattern-engine domain.
---

# Navigate the patterns

You build one output for one request. You use the patterns of this domain. You record what you did in `trace.json`.

## Files in this folder

| File | Content |
|---|---|
| `instructions.md` | The rules of the subject. Read it first. |
| `index.md` | All patterns: when to use them, when not to use them, their variants. |
| `patterns/<id>.md` | One pattern: its parts, params, asserts and rules. Read it before you use it. |
| `verify.mjs` | The check. Run `node verify.mjs`. |

## Workflow

1. Read `instructions.md` and `index.md`.
2. **Select.** Find the patterns whose "use when" matches the request. Start with the lowest level number: that is the top of the output.
3. **Exclude.** Read "do not use when" of each candidate. If it matches the request, use the pattern that it names.
4. If two patterns still match, select the one whose "use when" is more specific. Write the choice and the reason in `questions`.
<!-- ablate:no-nofit -->
5. If no pattern matches, or a "do not use when" names `no-fit`, go to "No pattern fits".
<!-- /ablate -->
6. **Variant.** Select the variant whose "when" matches the request.
<!-- ablate:no-divide -->
7. **Divide.** Read the `parts` of the pattern. For each part that is not a leaf, do steps 2–6 again for that part of the request. Continue until all parts are leaf parts.
<!-- /ablate -->
8. **Params.** If the request names a value for a param, use it. Otherwise use the default.
9. **Compose.** Write the output file. Follow `instructions.md` and the "Rules" of each pattern.
10. **Verify.** Run `node verify.mjs`. If it reports failures, fix the output and run it again. Stop after 3 runs.
11. **Trace.** Write `trace.json`.

<!-- ablate:no-nofit -->
## No pattern fits

Use this when no "use when" matches the request. Do not force a pattern that does not match.

1. Select the nearest pattern that is not a leaf. The nearest pattern has the most parts that the request needs.
2. List each change from the nearest pattern: parts that you add, remove or change.
3. Give the new thing an id, for example `lighthouse`. Build the output with that id as the top pattern. Use known leaf parts where you can.
4. Write `patterns/<id>.md` in the same format as the other pattern files, with `status: proposed` and `related: [<nearest>]`.
5. Run `node verify.mjs`. Write `trace.json` with `"outcome": "no-fit"`.
<!-- /ablate -->

## trace.json

```json
{
  "outcome": "match",
  "tree": { "p": "<top id>", "v": "<variant>", "params": { "<key>": "<value>" }, "parts": [ { "p": "...", "v": "..." } ] },
  "nearest": null,
  "deltas": [],
  "proposed": null,
  "questions": [],
  "attempts": 1
}
```

- `tree` holds the patterns that you used, without leaf parts. The order of `parts` is the order in the output.
- Put `params` only where the request names a value.
<!-- ablate:no-nofit -->
- For a no-fit run: `"outcome": "no-fit"`, `"nearest": "<id>"`, `"deltas": ["..."]`, `"proposed": "patterns/<id>.md"`, and `tree` for what you built.
<!-- /ablate -->
- `attempts` is the number of times that you ran `verify.mjs`.

## Rules

- Do not ask the user. If something is not clear, make the best choice and write it in `questions`.
- Do not change `index.md`, `instructions.md` or the existing pattern files.
- Write only `output` files and `trace.json`.
<!-- ablate:no-nofit -->
- For a no-fit run, also write one new pattern file.
<!-- /ablate -->
