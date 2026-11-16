# Pattern engine: instructions for agents

Read `README.md` first, then `docs/design/`.

## Layout

| Path | Content |
|---|---|
| `src/`, `tests/`, `PatternEngine.slnx` | The C# engine (in progress). See `docs/design/architecture.md` |
| `docs/design/` | Features, architecture, plan, decisions |
| `docs/pages/`, `docs/research/` | Explainer page sources and research |
| `spike/` | The Node spike. Parity reference for `shapes`. See `spike/README.md` |

## Rules

- Keep all files of this project in this folder.
- Follow the layout of the VSA framework (`Features/<Feature>/Slices/<Slice>/`, `<Name>Command`, `<Name>Result`, `<Name>Handler`), but do not reference VSA.Sdk or VSA packages.
- Put values in the settings file, not in code: modes, targets, limits, triggers.
- The core must not know a domain. Put subject code and data in `src/PatternEngine/Domains/<Name>/`.
- An agent call uses an agent file with `tools: StructuredOutput` and `output_schema`. Do not use `--bare`.
- Do not edit an agent file to make one case pass. Fix the pattern, the case or the graders.
- Do not change `spike/` except to keep it working as the reference.

## Before you report a change

1. Run `dotnet test`. All tests must pass.
2. If a phase has a parity test against the spike, run it.
3. Test agents with small runs (`--mode smoke` or `--mode dev`). Run `--mode full` only when the user asks for a validation.
