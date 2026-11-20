# Playground

A place for small tools, spikes and experiments, most of them made with AI agents. Each project is one folder at the root and holds everything it needs: source, docs, results and its own agent instructions. To add a project, make a new folder with a `README.md`, an `AGENTS.md` and a `CLAUDE.md`, and add one row to the project table in [AGENTS.md](AGENTS.md).

| Folder | Content |
|---|---|
| [pattern-engine/](pattern-engine/) | A generic engine for patterns, composition and evaluation. C# engine in progress; Node spike in `spike/` |
| [doc-kit/](doc-kit/) | One simple look for document blocks (tree, columns, graph, stack, folds). Notion export and artifact render. Spike done |
| [session-orchestrator/](session-orchestrator/) | A local supervisor for many Claude Code sessions: one writer for each session, free-text decisions, stacked default PRs |
