# Open questions and non-goals

[← Design index](README.md)

## Open questions

| Area | Question |
|---|---|
| Engine | Hybrid (code-led with escalation) or agent-led (topology B)? Phase A2 gives data |
| Engine | Keep the `modular` harness as a second engine, or only as the source of the escalation agent? |
| Engine | What does `partial` mean outside drawing: one leftover node, or a split of the request? |
| Engine | Depth and budget limits: per node, per run, or both? |
| Catalog | Promotion criteria: which evidence moves a pattern from `proposed` to `trial` to `stable`? |
| Catalog | Versions of patterns: do results name the catalog version they used? |
| Catalog | Authoring: an agent file that writes a pattern from the template, with a novelty check? |
| Pack | Second domain: logic puzzles or topology (routes on a small graph)? |
| Pack | One output file per run, or a set of files (needed for code)? |
| Graders | Is a judge grader needed for rules that asserts cannot express? |
| Graders | Is a vision grader needed for drawings, or is review by a person enough? |
| Cases | How is the outcome `ask` graded: any question, or a question about the right point? |
| Review | Do review notes become asserts and operators by hand, or through an agent? |
| Results | Keep result files in git, or only reports and baselines? |
| Code | Model calls through the Claude CLI (user login) or the Messages API (API key)? |
| Code | When to port: after stage 1, or in parallel with phase A3? |

## Non-goals

- **No one-skill harness.** Reason: the same quality as the modular harness, with an agent-written trace that drifts and a structure that is hard to change.
- **No fixed golden outputs for the agent.** Reason: a request has many correct outputs. The graders check properties, and golden outputs serve only to test the graders.
- **No values in code.** Reason: run sizes, models, targets and limits change often. They are settings.
- **No full eval runs while the system changes.** Reason: cost and time. Small runs show the direction; full runs validate.
