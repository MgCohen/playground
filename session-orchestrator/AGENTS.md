# Session orchestrator

Read `docs/plan.md` and `docs/store.md` before you change this project.

## Rules

- The store contract in `docs/store.md` is the interface between the mod and the supervisor. If you change a file in the store, change `docs/store.md` in the same commit.
- Record each test result and each finding in `docs/plan.md`.
- Keep the design page `docs/orchestrator-design.html` current, and publish it again to the same artifact URL after a change.
- Never use click questions. The user answers in free text.
- Test a live change in the scratchpad with its own `ORCH_STORE` and `ORCH_PORT`. Never use `store/` for tests.

## Tests

```bash
cd supervisor && npm test
```

## Load the mod

`~/.claude/settings.json` loads the mod for all sessions through `env.CLAUDE_CODE_PLUGIN_DIRS`, set to the absolute path of `mod/orch`.
