---
name: pattern-compose
description: Build the output file from plan.json, one pattern node at a time. Use it in the composer agent.
---

# Compose the output

1. Read `instructions.md`. It tells how to write the output and how to mark each pattern.
2. Read `plan.json`.
3. For each node of the plan, from the top: run `node pe.mjs parts <p> <v>`. Follow its `rules` and its `asserts`.
4. Write each node with its own mark (`data-p`, `data-v`) and its leaf parts in it. Keep the order of `plan.json`.
5. Run `node pe.mjs verify`. If it fails, fix the output and run it again. Max 3 runs.
6. Report: "done" and the last verify result.
