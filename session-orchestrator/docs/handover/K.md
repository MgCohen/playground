# K handover: architecture definition and topology review

State on 2026-11-25: the draft is complete and the work is stopped, because the user moves to another computer. The user deferred the approval and has not read the page. Do not ask for the approval, do not apply a default, and do not start K1.

## Start here

1. Check out `orch/k-roadmap`. It contains the full stack.
2. Read `docs/architecture.html`, the section "K result" in `docs/plan.md`, and this file.
3. Read "Known weak points" before you use a claim of the page.

| Item | Place |
|---|---|
| Page | `docs/architecture.html`, https://claude.ai/artifact/1ah9eGXuB8rxFzRfwJqGwU (private to the user) |
| Roadmap | `docs/plan.md`: "K result", and K1 to K6 in "Next tasks" |
| Link to the page | `AGENTS.md` |

## Goal and rules from the brief

- Define the architecture with the user: style, topology, guidelines, and architecture tests. The event stream is the pillar and must be at the center.
- Reason: the plan asked for one interface, `IProvider`. Sessions built an interface adapter for each feature.
- Out of scope: app code without a request of the user, the stream audit (W), the event store research (N), merges.
- Never touch the live store or port 7777.
- Results are HTML design pages in the project style, published as artifacts and saved in `docs/`. The outcome goes in `docs/plan.md`.
- Done when: the user approved the definition and guidelines, the artifact is published and in `docs/`, and the next tasks are in `docs/plan.md`.
- The brief permitted questions only in the K chat. The root replaced this rule: the user works only in the root session, so use `ask_user` and send plans as artifact links.

## Done and not done

| Item | State |
|---|---|
| Page with the tabs Context, Topology, Current, Decisions | Done, draft |
| Choices K-Q1 to K-Q9 | Defaults by timeout; the user answered none |
| Roadmap K1 to K6, link in `AGENTS.md` | Done, draft PR |
| Rows for K in `docs/decisions.md` | Not done |
| Brainstorm with the user | Not done; the user only asked for the page |
| Approval of the user | Deferred |
| Architecture tests, app code | Not started |

## Words of the user

- The user asked for a page in the usual style with these tabs: context (understanding, situation, expected output), expected topology with a suggestion, current situation, open questions. More tabs come as necessary.
- The user did not choose a branch. K made `orch/k-architecture` from `orch/d-data-upgrade`.
- Relayed by the root: the approval is deferred, no default, K1 does not start.
- There are no other answers of the user.

## Open question (deferred)

| Field | Content |
|---|---|
| Id | `toolu_01GLqNTQDZP6j32vVJvxN7BB` (in the store of the first computer) |
| Question | Do you approve the architecture definition and its guidelines (rules A1 to A9, tasks K1 to K6)? |
| Context | All nine choices are defaults with draft PRs. K is done at the approval |
| Options | Approve as it is; approve with changes (name them); refuse (name the parts to do again) |
| Recommendation sent | Approve, then start K1 |
| Recommendation of this file | Correct the weak points first |
| Default | None |

## The nine choices

| Id | Choice (default) | Other options |
|---|---|---|
| K-Q1 | (a) All state is a projection; exceptions: lease and heartbeat, cursors, logs, definitions | (b) Stream for facts, repositories for registry and run state; (c) all state, also leases |
| K-Q2 | (a) One `Orchestrator.Features` project, a folder for each slice | (b) A project for each slice; (c) slice folders in the projects of today |
| K-Q3 | (a) `IProvider` in `Kernel/Ports` | (b) `Providers.Abstractions`; (c) where it is |
| K-Q4 | (b) Events and `Contract.cs` queries | (a) Events only; (c) no limit |
| K-Q5 | (a) The supervisor dispatches events to slice reactors | (b) One central class |
| K-Q6 | (a) ArchUnitNET with NUnit, and the text scans | (b) NetArchTest.Rules; (c) own scans only |
| K-Q7 | (a) Plain .NET, copied kernel parts | (b) vsa-template packs |
| K-Q8 | (b) Slice by slice, an allow list that gets shorter | (a) One large move |
| K-Q9 | (a) The plugin is the live side of `Providers/ClaudeCode` | (b) A separate host |

## Stack, from the bottom to the top

All PRs are open drafts in https://github.com/MgCohen/playground. The base of each PR is the branch in the row above. No merge is approved.

| PR | Branch | Content |
|---|---|---|
| None | `orch/d-data-upgrade` | Base. K starts at `c445988`; the remote has 6 more commits |
| None | `orch/k-architecture` | Draft page |
| 85 | `orch/decision-toolu_016tK6CwXhnRwzCXCAi6uagV` | K-Q6 |
| 90 | `orch/decision-toolu_01WtdEdahocgiuYSe8MyaZ83` | K-Q1 |
| 92 | `orch/decision-toolu_01CiciSgEs3LKc5qRWV3iPLw` | K-Q2 |
| 94 | `orch/decision-toolu_013NPft93fGMNQJrwwgNbbWt` | K-Q3 |
| 95 | `orch/decision-toolu_014TmfDhuf6VN2d97fTr8yi7` | K-Q4 |
| 96 | `orch/decision-toolu_01Q2t8i6ga3sGKKUdKzdeH2o` | K-Q5 |
| 98 | `orch/decision-toolu_018akkXLhS6GQaRJjyyCo4Dv` | K-Q7 |
| 99 | `orch/decision-toolu_01BQp3Fj2jpb94EMj2gBNrDx` | K-Q8 |
| 100 | `orch/decision-toolu_0139yHia9D8qxCYVKF7in1PC` | K-Q9 |
| 101 | `orch/k-roadmap` | Roadmap, Decisions tab, this file |

`orch/session-e993ac42` has no K work. Its commit `153f2cf` is in `origin/main`.

## Known weak points

| Topic | Fact |
|---|---|
| Context | K read `AGENTS.md`, `ArchitectureTests.cs`, `IProvider.cs`, and parts of `Runners.cs` and `docs/plan.md`. K saw only headings of `store.md`, `how-it-works.html` and `orchestrator-design.html`. K did not read the content of `decisions.md`. The Sources table of the page claims more |
| `ProviderBoundaryTests` | Not read; the description comes from `plan.md`. "No test refused it" is an inference |
| C2, C4, C6 | Inferred from file names, line counts and `plan.md`. The files were not read |
| C1 | `ProviderTurns` only forwards (seen in the code). `IDocsChecker` and `IStateChecker` belong to neutral slices and can be valid test seams. Check before K2 deletes them |
| C3 | Supervisor gets Providers through Store, with a global using; it has no direct project reference |
| C5 | 11 files append events, without `Cli/Commands` and the tray mock. All use `store.Events`, so one write path exists. The issue is who appends (`Cli/Git`, `Web/Api`: A7) |
| K-Q4 and the A5 sample | `NotDependOnEachOther` also refuses events and `Contract.cs`. The rule must exclude them |
| K-Q5 and K-Q1 | A stored `Tick` event is high-rate data. A timer signal that is not stored removes the conflict |
| Leases | `LeaseRelease` is an event in the code, and the lease is on the exception list. Define the limit |
| Supervisor project | The topology puts it in `Orchestrator.Web`. No question covered this |
| Remote store | `Store/Remote` and the cloud workers (G2) were not analyzed |
| ArchUnitNET | Not installed. .NET 10 and Blazor support not checked. The sample did not run |
| A4 check | No ready rule finds a class that only forwards. It needs own code |
| C7 | The page says K1 closes it; `plan.md` says the page and `AGENTS.md` |
| Page look | Never examined in a browser |

## Next steps

1. Wait until the user brings up the approval.
2. Rebase on `orch/d-data-upgrade`, read the missing context, and verify C1 to C6 in the code.
3. Correct the page and `docs/plan.md`, with the results of W and N (K-Q1 depends on them).
4. Hold the brainstorm, and process the review of each default (`orch:orch-stack`).
5. Add the K rows to `docs/decisions.md`. After the approval, start K1.

## Only on the first computer

The transcript, the orchestrator store (open question, decision records), scratch copies of the page parts, and the worktree. The files in git contain all work. A store without these decision ids may refuse the `orch decision` commands; then use git and `gh`.
