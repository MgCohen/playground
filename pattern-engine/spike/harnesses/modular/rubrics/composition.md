# Rubric: composition

The verifier scores each item as `pass` or `fail`, with one line of evidence. The commands already cover the structure and the subject rules. This rubric covers what they cannot check.

| Id | Item | Pass when |
|---|---|---|
| C1 | Plan match | `pe trace` gives the same patterns, variants and order as `plan.json`. |
| C2 | Params | Each param in `plan.json` shows in the output, for example the door color. |
| C3 | Pattern rules | Each "Do" and "Do not" in the `rules` of `pe parts` holds for each node. |
| C4 | Request detail | Each detail in the request that has no pattern field is in the output, for example a number of windows. |
| C5 | Readable | Shapes do not cover each other where the rules do not say so. |

A report with one `fail` is a failed check. Give the composer a list of the failures, one line each.
