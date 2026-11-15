# Architecture

[← Design index](README.md)

The target design keeps the code-led solver for the normal path and adds an agent on call for exceptions. Code owns the loop, the checks and the trace. Each judgment is an agent file. A domain pack holds the subject behind one contract.

## Components

```mermaid
flowchart TB
    subgraph Core[Core: no domain words]
        CAT[Catalog] --> ENG[Engine]
        ENG --> TRC[Trace]
        EVAL[Eval] --> ENG
        EVAL --> GRD[Graders]
        TRUST[Grader trust] --> GRD
        REV[Review] --> GRD
        CFG[Settings]
    end
    subgraph Pack[Domain pack]
        PAT[Patterns + cases]
        ADP[Adapter]
        HOOK[Render, mutate, layout]
    end
    subgraph Harness[Harness: agent files]
        JUDGE[Judgment agents]
        ESC[Escalation agent]
    end
    PAT --> CAT
    ADP --> GRD
    HOOK --> ENG
    HOOK --> TRUST
    JUDGE --> ENG
    ESC --> ENG
```

| Component | Owns | Depends on |
|---|---|---|
| Catalog | Load, check, index, lifecycle of patterns | Pack patterns |
| Engine | Work queue, node states, compose and check loop, escalation | Catalog, pack hooks, agent files |
| Trace | The graph state of one run, as `trace.json` | Engine |
| Graders | The verdict of one run | Pack adapter |
| Grader trust | References, mutations, fixtures, regrade | Pack hooks, graders |
| Eval | Cases, runner, report, targets | Engine, graders, settings |
| Review | Review page, verdicts, agreement | Results, pack renderers |

## One request: the engine flow

```mermaid
flowchart TD
    Q[Work queue] --> C[Catalog query]
    C --> D[Decide: judgment agent]
    D -->|solved| K[Child nodes from parts]
    D -->|partial| K
    D -->|none| S[Scaffold proposed pattern]
    D -->|unclear| E[Escalation agent]
    K --> Q
    K -->|queue empty| L[Layout: pack]
    S --> L
    L --> M[Compose: judgment agent]
    M --> X[Check: code]
    X -->|fail| M
    X -->|retries used up| E
    E -->|resolved| Q
    E -->|question| A[Outcome: ask]
    X -->|pass| T[Assemble + trace]
```

The escalation agent gets the node, the state and the reason. It has tools to read more of the catalog and to run `pe` commands. It returns one of three results: a decision that goes back into the queue, a proposed pattern, or a question for the user. In an eval, a question is the outcome `ask`, and a case can expect it.

## Escalation triggers

All triggers are settings.

| Trigger | Source |
|---|---|
| The decide answer is "unclear" | Judgment agent |
| Two candidates are almost equal | Judgment agent gives a margin |
| A node is not allowed or too deep | Code |
| The check fails after the retries | Code |
| The request is outside the domain | Judgment agent |

## The measure flow

```mermaid
flowchart LR
    CS[Cases] --> R[Runner]
    R -->|one sandbox per run| ENG[Engine]
    ENG --> O[Output + trace]
    O --> G[Graders]
    G --> RP[Report + targets]
    RF[References pass] --> G
    MU[Mutations fail] --> G
    HV[Human verdicts] --> G
    RP -->|below target| FIX[Change patterns, cases, agents]
    FIX --> R
```

## Contracts

| Contract | Shape |
|---|---|
| Pattern file | Front matter (F1 fields) + Intent, Rules, Rationale |
| Agent file | `name`, `description`, `model`, `tools` (with `StructuredOutput`), `output_schema`; body is the system prompt |
| Pack adapter | `parse`, `lint`, `assert`, `checkLeaf`, `readParam` |
| Pack hooks | `reference`, `operators`, `layout`, `assemble`, `wrap`, `checkBox`, review renderers |
| Judge | `call(agent, input) → { data, ms, cost, error }` |
| Trace | `outcome`, `tree`, `nearest`, `deltas`, `proposed`, `questions`, `attempts`, `graph` |
| Run result | One record for each run: case, trace, grader results, pass, agent stats |

## Rules of the design

- Code owns the order of the steps, the checks, the retries and the trace.
- A model answers one question at a time, with only the data that code gives it.
- The agent files are the source of truth for a judgment: model, tools, schema, prompt.
- The same `pe` commands serve the agent and the system.
- Every value is a setting.
