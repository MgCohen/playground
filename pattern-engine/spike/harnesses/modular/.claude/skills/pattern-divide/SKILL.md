---
name: pattern-divide
description: Divide a selected pattern into its parts, recursively, and write plan.json. Use it from pattern-navigate after pattern-select.
---

# Divide into parts

Input: the selected top pattern `{ p, v, params }` and the request.

1. Run `node pe.mjs parts <p> <v>`. It lists the allowed parts with their `count` and `kind`.
2. For each part with kind `object` or `group`: find the objects that the request names for this part, in the order of the request. For each one, use skill `pattern-select` with only the allowed part ids. Then do step 1 again for it.
3. For each `leaf` part: use the count rule. Use the minimum count, unless the request names a number.
4. Write `plan.json` from `templates/plan.json`. Keep the order of the request.

```json
{ "p": "street", "v": "row", "params": {}, "leaves": { "ground": 1 },
  "parts": [ { "p": "house", "v": "chimney", "params": { "door_fill": "red" }, "leaves": { "window": 2 }, "parts": [] } ] }
```

Every `count` must be inside the range that `pe parts` gives.

## Then

This skill is one step. Do not stop here. Go back to the skill that called it and continue with its next step.
