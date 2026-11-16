# Pattern engine

A generic engine that turns a request into an output with a catalog of patterns, and measures how well it does that. Code owns the loop, the checks and the trace. Each judgment is an agent call. A domain pack holds the subject.

| Path | Content |
|---|---|
| [docs/design/](docs/design/README.md) | Features, architecture, code plan, decisions |
| `src/`, `tests/` | The C# engine (in progress, plan phase P0) |
| [spike/](spike/README.md) | The Node spike: two harnesses, the `shapes` domain, evals and grader trust |

Explainer pages: [the design](https://claude.ai/artifact/J6GKDVMJHgm7NutCcFz2pS) and [the spike](https://claude.ai/artifact/9BzBheCKxpXHhxLXBnrFhH).
