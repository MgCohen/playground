# Playground

A repository for small tools, spikes and experiments. Each project is one folder at the root.

## Rules

- Keep the root clean. The root holds only `README.md`, `AGENTS.md`, `CLAUDE.md` and one folder for each project.
- Put everything of a project inside its folder: source, docs, research, results, config, `.gitignore`, and its own `AGENTS.md` and `CLAUDE.md`.
- Do not put a file of a project at the root. Do not share files between projects. If two projects need the same code, copy it.
- Read the `AGENTS.md` of a project before you change it.
- A new project: make a folder with a `README.md`, an `AGENTS.md` and a `CLAUDE.md` that contains `@AGENTS.md`. Add one row to the table below and to the table in `README.md`.

## Projects

| Folder | Content |
|---|---|
| `pattern-engine/` | A generic engine for patterns, composition and evaluation. C# engine in progress; Node spike in `spike/` |
| `doc-kit/` | One simple look for document blocks (tree, columns, graph, stack, folds). Notion export and artifact render. Spike done |
| `session-orchestrator/` | A local supervisor for many Claude Code sessions: one writer for each session, free-text decisions, stacked default PRs |
