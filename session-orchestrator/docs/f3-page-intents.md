# F3 PR stack: the feature intent and the views

Status: the views are confirmed (2026-11-25). The sketches are in `docs/run-views/f3/`, tab F3 Sketches of the tracker. They wait for a review.

## Feature

F3 is a feature, not a page. The place of its views in the UI is decided later, when all features are unified. Until then, F3 is sketched as its own thing.

**Intent:** see the PRs of one work stream as they sit on each other, and approve them from the bottom.

- The tasks of a stream stack their PRs. A PR comes from a finished task, or from a decision that was made automatically.
- Task PRs and decision PRs both show.
- The earliest (lowest) PR comes first by default. The user can ask for a different PR.

## Views

| View | Goal | Content | State |
|---|---|---|---|
| S-1 Stack tree | See all PRs of a stream and their order | A Git tree, the base at the bottom. Each row: PR title, kind (task, decision), state. A tap opens S-3 | Confirmed |
| S-2 Stack cards | Approve or not, one PR at a time | One card for each PR, the lowest first, with the acts | Confirmed |
| S-3 PR preview | Read one PR and decide | The PR body, which follows a template: context, summary, the key information for the decision, fields to fill, a list of actions | Confirmed in outline |

Sketch variations: S-1 c shows the tree and the cards together. S-2 b shows all cards in a list.

## Rules

- Approve also merges for now. The relation of approve and merge becomes configurable later (O-4).
- On a PR that is not the lowest, all acts are available: view, comment, reject, pre-approve. A pre-approved PR does not merge yet.
- A PR follows one template with three forms: card, preview and full. F4 is the full form.

## Open items

| Id | Item |
|---|---|
| Q-S6 | Is "comment" the same act as "change"? |
| O-2 | The PR body template: its fields and its list of actions. Not defined yet |
| O-3 | Where the views live (page, stream view, session page). Decided at the unification of the features |
| O-4 | The settings for approve and merge |

## Ideas, not in this round

- A page of all open PRs of a project. It is a different page from F3 and F4.

The spec in `docs/review-pages-spec.html` (tab F3) has the rules of today: PR states, blockers and `orch merge`.
