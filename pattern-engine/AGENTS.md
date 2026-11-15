# Pattern engine: instructions for agents

Read `README.md` first. It has the layout, the commands, the adapter contract and the settings.

## Rules

- Keep all files of this project in this folder.
- Put values in `eval.config.yaml`, not in code: run modes, targets, grader-test settings.
- The core (`core/`) must not know a domain. Put subject logic in `domains/<name>/`.
- Do not edit the agent files of a harness to make one case pass. Fix the pattern, the case or the graders.

## Before you report a change

1. Run `node core/graph/build.mjs shapes`. Fix each error.
2. Run `node core/eval/self-test.mjs shapes`. All grader checks must pass.
3. If a pattern or an assert changed, run `node core/eval/regrade.mjs results/<run>.json` on the last full run.
4. Test with `--mode smoke` or `--mode dev`. Run `--mode full` only when the user asks for a validation.

## Docs

- `docs/pages/`: the sources of the explainer pages (design, spike).
- `docs/research/`: the research reports and notes behind the design.
