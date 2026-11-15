# Pattern engine (spike)

Explainer pages: [the spike](https://claude.ai/artifact/9BzBheCKxpXHhxLXBnrFhH) and [the design](https://claude.ai/artifact/CzP6ottg8Wt9Aa5oMR7Gkd).

An agent selects patterns, divides a request into parts, composes an output, verifies it and writes a trace. The eval runs the agent many times and grades each run. A **domain pack** sets the subject. The **core** does not change between domains.

```
request → index → select → exclude (do_not_use_when) → variant → divide (recursive) → compose → verify (max 3) → trace
                    └─ no match → nearest pattern + deltas → proposed pattern file
```

## Layout

| Path | Owner | Content |
|---|---|---|
| `core/lib/` | core | Load and validate patterns, trees, domain loading |
| `core/graph/build.mjs` | core | Validate patterns; write `build/graph.json` and `build/index.md` |
| `core/graph/generate-cases.mjs` | core | Make eval cases from the graph and the `phrase` fields |
| `core/skills/navigate/SKILL.md` | core | The workflow that the agent follows |
| `core/eval/` | core | Sandbox, agent call, graders, verify, runner, report, grader self-test |
| `domains/shapes/` | pack | `domain.yaml`, `instructions.md`, `adapter.mjs`, `patterns/`, `cases/`, `fixtures/` |
| `results/` | output | One JSON file for each eval. `results/runs/` holds the outputs (not in git) |

## The adapter contract

The core calls only these functions of `domains/<name>/adapter.mjs`:

| Function | Returns |
|---|---|
| `parse(text)` | `{ roots: Node[], errors }`. A node has `p` (pattern id), `v` (variant) and `children` |
| `lint(parsed)` | `{ ok, failures }` for the subject rules |
| `assert(node, asserts)` | `[{ assert, ok, detail }]` for the asserts in a pattern file |
| `checkLeaf(node, pattern)` | A list of problems with one leaf part |
| `readParam(node, key, def)` | The value of one param in the output, for example a color name |

## Commands

Run from this folder.

```bash
node core/graph/build.mjs shapes
```

```bash
node core/eval/self-test.mjs shapes
```

```bash
node core/eval/run.mjs shapes --mode smoke
```

```bash
node core/eval/run.mjs shapes --mode dev --dry
```

```bash
node core/eval/run.mjs shapes --mode dev --ablate no-dont-use --only near-miss
```

```bash
node core/eval/run.mjs shapes --mode full --label baseline
```

```bash
node core/eval/report.mjs results/<run>.json results/<baseline>.json
```

## Harnesses

A harness is the set of agent files that the sandbox gets. The cases, graders and models stay the same, so two harnesses can be compared.

| Harness | Files | Use |
|---|---|---|
| `monolith` | `harnesses/monolith/SKILL.md`: one skill with the full workflow | Frozen control. Do not change it. Use it only to compare |
| `modular` | `harnesses/modular/`: a router skill, 4 step skills, a `composer` and a `verifier` agent, a rubric, 4 templates | **The default.** One file for each role, step, rubric and output shape |

Select one with `--harness monolith|modular`. Add `--gate` to make the system run `pe verify` after the agent and, on a failure, start the agent once more with the failures.

### The `pe` commands

The agent runs these in its sandbox. The runner calls the same functions in `core/eval/pe-cli.mjs`.

| Command | Result |
|---|---|
| `node pe.mjs index` | The pattern index as JSON |
| `node pe.mjs parts <id> [variant]` | Parts for the variant, params, asserts, rules |
| `node pe.mjs verify [--json]` | Structure and rules checks of the output |
| `node pe.mjs trace [--write]` | The pattern tree of the output; `--write` puts it into `trace.json` |
| `node pe.mjs scaffold <id> --from <id>` | A proposed pattern file from the template |

## Run modes

Use a small, cheap run while you change the system. Use a full run only to validate.

| Mode | Use it to | Cases | k | Model | Agent runs |
|---|---|---|---|---|---|
| `smoke` | Check that the loop runs | 3, one from each rare bucket | 1 | haiku | 3 |
| `dev` (default) | See if a change is better or worse | 8, spread over the buckets | 1 | haiku | 8 |
| `full` | Validate, set a baseline | all | 3 | sonnet | 63 |

A flag changes one value of the mode: `--sample n`, `--k n`, `--model haiku|sonnet|opus`. `--dry` lists the cases and the run count and runs nothing. The sample takes cases in this order of buckets: no-fit, near-miss, deep, group, object.

Other options: `--only gen|hand|near-miss|no-fit|<case id>`, `--limit n`, `--concurrency 4`, `--ablate no-dont-use,no-divide,no-nofit`, `--label text`.

The report shows the API-equivalent cost and the time for each run. Measured: haiku about $0.02 per run, sonnet about $0.1, opus about $0.5.

## Settings

All values are in `eval.config.yaml`. A domain can override any key in `domains/<name>/eval.config.yaml`.

| Key | Controls |
|---|---|
| `modes` | Sample size, k and model for `smoke`, `dev`, `full` |
| `targets` | The minimum (or `{ max }`) rate for each metric, for each mode. A run below a target prints `BELOW TARGET` and exits with code 1 |
| `grader_tests` | Target rates for the grader test, the mutation operators to use, and the case sets |

Target ids: `selection`, `pass@1`, `pass^k`, `near-miss`, `no-fit`, `trace`, `agent-errors`.

## Grader tests

The graders are tested on outputs whose verdict is known, before they grade an agent.

| Test | Mechanism | Must be |
|---|---|---|
| Known good | The pack draws each expected tree (`reference.mjs`). The graders grade the drawing | Pass |
| Known bad, generated | Each operator in `mutations.mjs` breaks each reference drawing in one way | Fail, in the grader that the operator names |
| Known bad, by hand | `fixtures/*.svg` with `fixtures/expect.yaml` | The verdict in `expect.yaml` |

```bash
node core/eval/self-test.mjs shapes
```

```bash
node core/eval/regrade.mjs results/<run>.json
```

`regrade` grades saved agent outputs again with the current patterns and graders, with no agent runs. Use it after a change to an assert.

An operator has an `id`, the graders that must catch it (`expect`), and `apply({ svg, tree, adapter })`, which returns the broken output or `null`. A missed operator shows a hole in the graders. Fix the hole in the pattern asserts or the adapter, not in the operator.

## Human review

A review page shows each run as one card: the request, the output, the expected answer, what the agent reported, and the engine verdict. A person approves or rejects each run. The page then shows how often the engine and the person agree.

```bash
node core/review/build.mjs results/<a>.json results/<b>.json --title "Shapes Full Review" --out results/review/<name>.html
```

| Part | Owner | File |
|---|---|---|
| Builder: reads result files, makes the page data | core | `core/review/build.mjs` |
| Page: card, filters, keys, swipe, shared store | core | `core/review/template.html` |
| Renderers: output, expected answer, trace | pack | `domains/<name>/review.mjs` (a text fallback is used if it is missing) |

Publish the page as an artifact with the `db` capability. Verdicts go into the `verdicts` collection, one document for each run, so they can be read back to measure the engine.

## Graders

| Grader | Type | Checks |
|---|---|---|
| selection | core | `trace.json` against the case: outcome, tree, variants, params, nearest |
| built | core | The tree in the output file against the case |
| structure | core + adapter | Known patterns, variants, part counts, leaf form, pattern asserts |
| rules | adapter | The subject rules (`lint`) |
| proposed | core | For no-fit cases: a valid `proposed` pattern file that names the nearest pattern |

A run passes when all graders pass. `pass^k` counts the cases where all k runs pass.

## Agent sandbox

Each run gets a new folder in the system temp folder. It holds only `SKILL.md`, `instructions.md`, `index.md`, `patterns/` and `verify.mjs`. The agent cannot read the cases or the graders. The agent runs as `claude -p` with no user settings, plugins, MCP servers or skills, and with these tools only: Read, Write, Edit, Glob, Grep and `node verify.mjs`.

## Not in the spike

- The LLM judge and its calibration.
- A render step (SVG to PNG).
- The `ui` and `code` packs.
