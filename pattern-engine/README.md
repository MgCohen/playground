# Pattern engine

A generic engine that turns a request into an output with a catalog of patterns, and measures how well it does that. Code owns the loop, the checks, the limits and the trace. Each judgment is an agent call. A domain pack holds the subject.

| Path | Content |
|---|---|
| [docs/design/](docs/design/README.md) | Features, architecture, plan, decisions, build log |
| `src/PatternEngine/` | The C# engine: `App/Host`, `Plugins/`, `Features/`, `Domains/` |
| `agents/` | The agent files: decider, composer, escalation, pattern-author; for composition: composer-decider, grader, decomposer |
| `tests/PatternEngine.Tests/` | Unit, scripted engine and parity tests (no model) |
| `pe.settings.yaml` | Every value; a pack overrides it in `Domains/<Pack>/pack.settings.yaml` |
| `results/` | Result files (in git); run outputs and review pages are not in git |
| [spike/](spike/README.md) | The Node spike: the parity reference for `shapes` |

Domains: `shapes` (SVG drawings), `circuits` (logic circuits, graded by a truth table), `topology` (design patterns with typed ports, graded by data flow).

Composition has its own loop in `Features/Compose` and its own domain, `Domains/Party`: a party of heroes for one or more quests, with no ports. See [the composition plan](docs/design/plans/composition.md).

## Commands

Build once with `dotnet build`. The tool is `src/PatternEngine/bin/Debug/net10.0/pe`.

```bash
pe build [domain]                 # validate the patterns; write build/index.md and build/graph.json
pe trust [domain]                 # test the graders with no model: fixtures, references, mutations
pe cases [domain]                 # list the cases
pe solve "Draw a house." [--ask]  # one request; --ask lets you answer the questions of the engine
pe eval [domain] --mode dev       # small eval with Haiku; --dry lists the cases only
pe report results/<run>.json [baseline.json]
pe regrade results/<run>.json     # grade stored outputs again, no agent calls
pe review results/<run>.json      # the review page: one card for each run
pe propose <run folder>/<id>.md   # a proposed pattern of a run goes into the catalog
pe approve <id>                   # a person approves: proposed -> trial
pe trials [domain]                # metrics of each trial pattern; notifications at the thresholds
pe stable <id> | pe remove <id>   # the final call of a person
pe compose eval --rung budget --strategy compare   # party cases with the compose loop; the oracle grades each run
pe compose grader-check           # the grader agent on fixed pairs, against the oracle
```

Run the tests with `dotnet test`. The agent calls use the Claude CLI with your login.

Explainer pages: [the design](https://claude.ai/artifact/J6GKDVMJHgm7NutCcFz2pS) and [the spike](https://claude.ai/artifact/9BzBheCKxpXHhxLXBnrFhH).
