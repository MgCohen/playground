# Features

[← Design index](README.md)

Each feature has the same blocks: purpose, state, tests that exist, findings from the spikes, and pending tests. All measured values come from `results/` and are small samples on the `shapes` domain.

## Summary

| Feature | Code today | Tests that exist | Pending tests |
|---|---|---|---|
| F1 Catalog | `core/lib/patterns.mjs`, `core/graph/build.mjs` | Build validation | Validator unit tests, promotion |
| F2 Domain pack | `domains/shapes/` | Self-test, reference drawings | Second domain |
| F3 Engine | `harnesses/`, `core/solve/` | Eval runs | "Ask" cases, escalation, scripted loop |
| F4 Trace | `pe trace`, solver graph | Trace-matches-output metric | Rule for agent notes |
| F5 Cases | `core/graph/generate-cases.mjs`, `cases/` | Ablations | "Ask" and scale cases |
| F6 Graders | `core/eval/graders/` | Self-test | Judge calibration |
| F7 Grader trust | `core/eval/mutate.mjs`, `fixtures/` | References, mutations | Operator coverage |
| F8 Runner | `core/eval/run.mjs`, `agent.mjs` | Smoke runs | Unit tests of the sample |
| F9 Report + targets | `core/eval/report.mjs`, `eval.config.yaml` | Target check on old results | Target calibration |
| F10 Human review | `core/review/` | Browser check | Agreement from real verdicts |
| F11 Settings | `core/lib/config.mjs` | Indirect | Merge unit tests |

<details>
<summary><b>F1 Catalog</b> · pattern files, validator, index, lifecycle</summary>

**Purpose.** Hold the patterns of one domain, check them, and give the engine an index.

**State.** One Markdown file for each pattern. The front matter has `id`, `kind` (leaf, object, group), `level`, `status`, `phrase`, `use_when`, `do_not_use_when` (`when`, `use`), `variants`, `parts` (`ref`, `count`, `only`), `params`, `related`, `asserts`. The body has Intent, Rules and Rationale. The build checks links, levels, cycles, counts, variant names, params and sections, and writes `index.md` and `graph.json`. The status values are `proposed`, `trial` and `stable`; only `trial` and `stable` go into the index.

**Tests that exist.** `node core/graph/build.mjs shapes` fails on each rule. No unit test file.

**Findings.**
- Two `do_not_use_when` rules can point at each other (house ↔ tower). An agent looped between them. The validator warns about such pairs.
- `use: no-fit` in a rule is the only way to say "no pattern fits this case".
- A scaffold must not copy parts that belong to one variant (`only`). It made a proposed pattern require a chimney.

**Pending tests.** A unit test for each validator rule, with a broken file for each. A test of the promotion path from `proposed` to `trial` to `stable`, which does not exist in code.
</details>

<details>
<summary><b>F2 Domain pack</b> · the subject, behind one contract</summary>

**Purpose.** Put everything about a subject in one folder, so the core does not change between subjects.

**State.** The `shapes` pack has `domain.yaml`, `instructions.md`, `patterns/`, `cases/`, `fixtures/` and five code files:

| File | Gives the core |
|---|---|
| `adapter.mjs` | `parse`, `lint`, `assert`, `checkLeaf`, `readParam` |
| `reference.mjs` | A drawing of an expected tree (known good) |
| `mutations.mjs` | Operators that break a drawing (known bad) |
| `solve.mjs` | Layout boxes and group assembly for the solver |
| `review.mjs` | HTML for the review card |

**Tests that exist.** The self-test runs the fixtures, the references and the mutations through the adapter.

**Findings.** The pack contract has 5 code files and about 12 functions. Some core code assumes one output file and a tree of marked groups (`data-p`).

**Pending tests.** A second pack on a subject that is not drawing. Every core change that it needs is a leak of the first domain into the core.
</details>

<details>
<summary><b>F3 Engine</b> · request in, output out</summary>

**Purpose.** Select patterns, divide the request into parts, compose the output and check it.

**State.** Two harnesses:

| Harness | Who runs the loop | Cost / run | Time / run |
|---|---|---|---|
| `modular` | An agent session with 5 skills, a composer and a verifier agent | $0.021 (Haiku), $0.24 (Sonnet) | 79–94 s |
| `solver` | Code; one agent file for each judgment (`decider`, `composer`) | $0.003 (Haiku) | 21 s |

**Tests that exist.** Eval runs. Full compare on the tuned set (Sonnet, k=3): modular 75/75. Held-out set (Haiku, 20 cases, k=2): solver 38/40 (both failures had one scaffold cause; see F1), modular 10/11 before the run stopped.

**Findings.**
- The cost of the modular harness is its subagents. Each new context carries about 20k tokens on each turn.
- A step skill that replies with a message ends a headless run. Steps hand off through files.
- A background subagent adds a serial wait of about 30 s.
- A plain judgment call (agent file, `tools: StructuredOutput`, `--json-schema`) is about 1.5k tokens and 2.4 s. A `tools` list without `StructuredOutput` stops the schema.
- The solver depends fully on the rules: without `do_not_use_when`, its near-miss score falls from 100% to 0%.
- Blind spots of the code-led path: an unclear request gets a silent choice; an unresolved node is dropped; failed retries give a bad output. Nobody reasons, and nobody asks.

**Pending tests.** "Ask" cases with an escalation agent. A scripted judge test of the solver loop with no model. A scale test with 10 or more objects.
</details>

<details>
<summary><b>F4 Trace</b> · what the engine did, as data</summary>

**Purpose.** Record which patterns, variants and params the engine used, the no-fit data and the questions.

**State.** `trace.json` with `outcome`, `tree`, `nearest`, `deltas`, `proposed`, `questions`, `attempts`. The solver writes it from its graph state. The modular harness writes the tree with `pe trace --write`.

**Tests that exist.** The metric "trace matches output" compares the trace tree with the tree in the output.

**Findings.** An agent that writes the trace by hand was wrong in 20 of 75 runs: it added leaf parts and changed their order. Code that writes the tree was right in 75 of 75.

**Pending tests.** A rule for what an agent may add to the trace (notes and questions only), with a check.
</details>

<details>
<summary><b>F5 Cases</b> · what the eval asks</summary>

**Purpose.** Give the eval requests with known correct answers.

**State.** A generator walks the graph and writes requests from the `phrase` fields, with the expected tree. Settings: count, seed, largest group. Hand cases in YAML carry tags: `near-miss`, `near-miss-easy`, `no-fit`. Another seed gives a held-out set (`heldout` tag). The sample takes the rare tags first.

**Tests that exist.** Ablations prove that the cases test a part. Without `do_not_use_when`: near-miss 100% → 50% (modular) and → 0% (solver). Without the no-fit text: no-fit full pass 100% → 0%.

**Findings.**
- The first near-miss cases were too easy: the words already named the right pattern. Good near-miss cases need the rule.
- The tuned set is fitted to the fixes. A score close to 100% on it does not show quality on new requests.
- One hand case had a wrong expected answer (tent → shed is a fair answer).

**Pending tests.** "Ask" cases: an unclear request, a request outside the domain, two equal patterns. Scale cases with 10 or more objects.
</details>

<details>
<summary><b>F6 Graders</b> · deterministic checks of one run</summary>

**Purpose.** Decide if one run passed, with code.

**State.** Five graders. A run passes when all pass.

| Grader | Compares |
|---|---|
| selection | The trace with the expected tree |
| built | The tree in the output with the expected tree |
| structure | Patterns, variants, part counts, leaf form, asserts |
| rules | The subject rules |
| proposed | A valid proposed pattern (no-fit cases) |

**Tests that exist.** Self-test: hand fixtures and unit checks of the selection grader.

**Findings.** The graders read data, not pictures. A yellow door on a yellow body passes. The assert language covers geometry, counts, overlap and order.

**Pending tests.** If a judge or a vision grader is added: calibration against human verdicts.
</details>

<details>
<summary><b>F7 Grader trust</b> · the graders are tested before they grade</summary>

**Purpose.** Prove that the graders pass good outputs and fail bad outputs for the right reason.

**State.** Known good: the pack draws each expected tree, and the graders must pass it. Known bad: 13 operators each break each drawing in one way, and the graders must fail it in the grader that the operator names. Hand fixtures with `expect.yaml`. `regrade` grades saved outputs again with the current graders.

**Tests that exist.** References 82/82 pass. Mutations 813/813 caught, all for the right reason.

**Findings.** The first mutation run found a hole: a window on top of the door passed in 72 of 72 cases. One assert closed it. Regrade of 150 saved outputs found no output with the problem.

**Pending tests.** A check that each assert in the catalog has at least one operator that it alone catches.
</details>

<details>
<summary><b>F8 Runner</b> · many runs, isolated</summary>

**Purpose.** Run the engine on many cases, each in a clean sandbox, and keep the outputs.

**State.** Modes `smoke`, `dev`, `full`; flags for sample, k, model, harness, ablation, gate, concurrency and case set; `--dry`. One sandbox folder for each run in the system temp folder. The agent cannot read the cases or the graders. The CLI runs on the user's login.

**Tests that exist.** Smoke runs on each harness.

**Findings.** A run is a chain of model turns, so its time is turns × about 2–3 s. Wall time = runs × time ÷ concurrency. The gate (code runs `verify` after the agent) repaired 2 of 8 failed modular runs.

**Pending tests.** Unit tests of the sample and of the case filters.
</details>

<details>
<summary><b>F9 Report + targets</b></summary>

**Purpose.** Turn one run set into metrics, compare with a baseline, and fail on targets.

**State.** Metrics: selection, built, structure, rules, pass@1, pass^k, near-miss, no-fit precision, no-fit full pass, agent errors, trace matches output, gate retries, cost, time, turns, subagents. Targets for each mode in `eval.config.yaml`; below target gives exit code 1.

**Tests that exist.** Target checks on stored results: the full runs meet the targets; an ablated run fails them.

**Findings.** None.

**Pending tests.** Target values from a baseline on a held-out set.
</details>

<details>
<summary><b>F10 Human review</b></summary>

**Purpose.** Let a person judge each output against the expected answer, and measure the agreement with the engine.

**State.** A page with one card for each run: request, engine verdict, agent output, expected drawing (from the reference renderer). Approve, reject, report, back, skip, details. Verdicts go to the shared store of the page. The builder is core; the renderers are in the pack.

**Tests that exist.** Browser checks at phone and desktop width.

**Findings.** The expected drawing is one correct form, drawn from the fixture. It was not part of the run.

**Pending tests.** Agreement from real verdicts. A path from report notes to new asserts and operators.
</details>

<details>
<summary><b>F11 Settings</b></summary>

**Purpose.** Keep every value out of the code.

**State.** `eval.config.yaml` at the engine root, merged with an optional file in the pack: harness, modes, targets, grader tests, solver settings.

**Tests that exist.** Indirect, through the runs.

**Pending tests.** Unit tests of the merge and of the target lookup.
</details>
