# Handover N: event store product research

The research is not started: the user deferred it. No app code, document, artifact or PR exists from this task. A fresh session continues from this file and the files that it names.

## State

| Item | State |
|---|---|
| Context read (on `orch/d-data-upgrade`) | Done |
| Question to the user | Sent, deferred by the user |
| Brainstorm with the user | Not done |
| Web research | Not done |
| Comparison page and artifact | Not done, no link |
| Roadmap tasks in `docs/plan.md` | Not done |

## Task

Goal: brainstorm with the user. Then find if an event store product can hold the event stream and the store data, and if it helps. The user called the product "ES4" in voice.

The user also asks two things:

- Does it work with other storage providers, such as Notion (the planned version 2 store)?
- Does it give real benefits here?

In scope: brainstorm, research, comparison, recommendation. Out of scope: app code, installs, accounts, the architecture (session K), the stream audit (session W), merges.

Done when:

1. The user discussed the research and accepted a recommendation.
2. The artifact is published and its HTML page is in `docs/`.
3. The next tasks are in `docs/plan.md`.

## Rules

| Rule | Source |
|---|---|
| Start the research only after the user answers | User, through root |
| The question stays open, with no default | User, through root |
| The user works only in the root session | User, through root |
| No `update`; `report` only on request or when done | Brief |
| No touch of the live store or port 7777 | Brief |
| No installs or accounts without approval | Brief |

The brief says to ask only in the session chat, with no `ask_user`. The later rule (root session only) replaced that part, so the question went through `ask_user`. The message with the later rule arrived cut off: its other text is not known.

The result page follows the usual style of the project: an HTML comparison, published with the Artifact tool. Web search is allowed. Share findings with K and W through the root.

## Answers of the user

Only one: the question is deferred, with no default and no research. There is no product name and no problem statement.

## Context for the research

Sources: `docs/plan.md` ("H1 plan", "H2 result", "H3 result"), `docs/store.md`, `docs/decisions.md`, `docs/storage-options.html`, `src/Orchestrator.Core/Storage`, `src/Orchestrator.Store`.

| Constraint | Effect on a product |
|---|---|
| `IRepository<T, TId>` for each data kind | A product is one more implementation |
| `IAppendFeed<T>`, `IStreamSignal` | Map to ordered read and subscription |
| Create fails on an existing id | Needs an atomic create |
| Save of an old version fails | Needs optimistic concurrency |
| Contract tests for each implementation | A product must pass them |
| Plugin hooks write local files | Hook budget is 10 s |
| Heartbeat each 3 s stays local | Leases stay out of the product |
| Notion mirror: never | Notion is the store, or is not used |
| Stack: C# on .NET 10 | Needs a .NET client |

Volume in `docs/plan.md` on 2026-11-17: 3,654 envelopes in 2 days. On that day, 88 % of 2,350 were `session.tool`, `session.output` and `session.input`.

`H2 result` says that the store code is synchronous. A remote implementation must examine the blocking waits.

Candidates to compare (guesses, not researched): KurrentDB (was EventStoreDB), Marten on PostgreSQL, Eventuous, NEventStore. The local folder and Notion are the baselines.

## Next steps

1. Fetch `origin`. Start from the branch that holds the current `docs/plan.md`, and bring this file.
2. Wait until the user lifts the deferral. Do not ask the question before that.
3. After the answers, discuss the scope with the user.
4. Do the research against the constraints table.
5. Write the comparison page in `docs/`, and publish it as an artifact.
6. Add the outcome and the next tasks to `docs/plan.md`.

## Branches and PRs

| Item | Value |
|---|---|
| Branch | `orch/session-393f41e3` |
| Base commit | `153f2cf` (in `origin/orch/d-data-upgrade`) |
| PR | None |
| Artifact | None |
| Context branch | `origin/orch/d-data-upgrade` at `f8b3f8a` |

The context was read from a local copy of that branch that is 6 commits older (`c445988`). Read the current files again.

## Only on the first computer

- The transcript of session `393f41e3` and its brief. This file holds their content.
- The open question in the local store: id `toolu_01Kyfg229qkmVkHzQRV1LzLG`.
- No scratchpad result, process or monitor.

## Open questions

Deferred: do not ask it until the user lifts the deferral. Full text as sent:

> What does "ES4" mean, and which problem must the event store product solve?

1. Which product? Guesses: KurrentDB (was EventStoreDB, a server), Marten (event sourcing on PostgreSQL), Eventuous or NEventStore (.NET libraries for many databases).
2. Which problem must it solve? Examples: fewer files, faster queries, subscriptions, replay of history, cloud or phone access.
3. Is a local server or container (KurrentDB or PostgreSQL) acceptable? The other choice is a store that is only files or only cloud.
4. Must the product store its data in Notion? The other choice is Notion for documents and the product for events.

| Field | Value |
|---|---|
| Option 1 | KurrentDB (EventStoreDB) as the main item, with a short list |
| Option 2 | Marten on PostgreSQL as the main item, with a short list |
| Option 3 | Another product: give the name |
| Option 4 | Compare a short list of all of them |
| Recommendation | Short list, with the "ES4" product as the main item |
| Risk | medium |
| Default | None |
