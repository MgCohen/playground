# Decisions, open questions and non-goals

[← Design index](README.md)

## Terms

- **Agent call:** one judgment step. It uses an agent file with `tools: StructuredOutput` and an `output_schema`, with minimal tools and context. It is not a plain model call.

## Decisions

| # | Area | Question | Decision |
|---|---|---|---|
| Q1 | Engine | Hybrid or agent-led? | Hybrid. The balance between code and agents is designed with the owner before phase A2: which element is code, which is an agent, who starts whom |
| Q2 | Engine | Keep the `modular` harness? | One engine: a modular hybrid, made of small files that a person can edit. The `modular` harness stays as a reference for the next rounds. It is not ported |
| Q3 | Engine | What does `partial` mean? | A `partial` pattern covers a part of the requirements. Other patterns cover the rest. The decider names the requirements that stay open |
| Q4 | Engine | Depth and budget limits | Both. For each node: depth, retries, escalations. For each run: agent calls, cost, time. A stop writes a stop reason in the trace |
| Q5 | Catalog | Promotion criteria | A person approves `proposed` → `trial`. A `trial` pattern is in the index and in use. Code records its metrics and sends a notification at a good or a bad threshold. A person makes the final call: `stable` or removal |
| Q6 | Catalog | Versions of patterns | Each result records the catalog hash and a hash for each pattern that it used. An edit to a `trial` pattern restarts its metrics |
| Q7 | Catalog | Authoring | An author agent writes the proposed pattern during the run, with an authoring guide file. Code runs the validator and a novelty check. The draft goes into the Q5 flow |
| Q8 | Pack | Second domain | Logic circuits first, then software topology (design patterns with typed input and output) |
| Q9 | Pack | Output files | One output file for each run |
| Q10 | Graders | Judge grader | First choice: rules that code can infer. Patterns declare typed inputs and outputs, and code simulates the output. A judge only for the other rules: it records verdicts as information and becomes a grader after calibration |
| Q11 | Graders | Vision grader | Not now. Later: analyzers for each output type (vision for drawings, code quality for code), as a pack hook |
| Q12 | Cases | Grade of `ask` | `ask` is a turn. A case has hidden facts. The runner acts as the user and answers from them ("no preference" if a fact is missing). The graders check needed questions, extra questions, use of the answer and the number of rounds |
| Q13 | Review | Notes | An open channel. Any reviewer, person or agent, can add notes to a case, a fixture or an output. The eval can read them. Roles and the path from notes to asserts come later |
| Q14 | Results | Results in git | Keep the current setup: result files in git, run outputs outside git |
| Q15 | Code | CLI or API | The Claude CLI with the subscription login, behind `IJudge`. Do not use `--bare`: it ignores the subscription login |
| Q16 | Code | When to port | Now. The new work is built directly in C# with the VSA framework. The Node spike is the parity reference for `shapes` |

## Open questions

| Area | Question |
|---|---|
| Engine | The map of code and agents: which element is code, which is an agent, who starts whom (design step with the owner, before A2) |
| Roles | Which roles are agents and which are people: reviewer, author, approver, grader |
| Review | How notes become asserts and operators |
| Code | How a domain pack fits the VSA layout: a feature module, a plugin module, or a separate assembly that the app loads |
| Code | Does a persistent `stream-json` CLI process keep `--agent` and `--json-schema` for each message? Test it in B3 |

## Non-goals

- **No one-skill harness.** Reason: the same quality as the modular harness, with an agent-written trace that drifts and a structure that is hard to change.
- **No second engine.** Reason: one modular hybrid engine. The old harnesses are references only.
- **No fixed golden outputs for the agent.** Reason: a request has many correct outputs. The graders check properties and behavior. Golden outputs serve only to test the graders.
- **No values in code.** Reason: run sizes, models, targets and limits change often. They are settings.
- **No full eval runs while the system changes.** Reason: cost and time. Small runs show the direction; full runs validate.
