# F3 PR stack: the feature intent and the views

Status: the intent pass is in progress. The sketches follow after the views are confirmed.

## Feature

F3 is a feature, not a page. The place of its views in the UI is decided later, when all features are unified. Until then, F3 is sketched as its own thing.

**Intent:** see the PRs of one work stream as they sit on each other, and approve them from the bottom.

- The tasks of a stream stack their PRs. A PR comes from a finished task, or from a decision that was made automatically.
- Task PRs and decision PRs both show.
- The earliest (lowest) PR comes first by default. The user can ask for a different PR.

## Views

| View | Goal | Content | State |
|---|---|---|---|
| S-1 Stack tree | See all PRs of a stream and their order | A Git tree, the base at the bottom. Each row: PR title, kind (task, decision), state. A tap opens S-3 | Proposed |
| S-2 Stack cards | Approve or not, one PR at a time | One card for each PR, the lowest first, with the acts | Proposed |
| S-3 PR preview | Read one PR and decide | The PR body, which follows a template: context, summary, the key information for the decision, fields to fill, a list of actions | Confirmed in outline |

## Open items

| Id | Item |
|---|---|
| Q-S2 | Does approve also merge the PR? |
| Q-S3 | Which acts are available on a PR that is not the lowest? |
| Q-S4 | Is S-3 the same view as the F4 PR page? |
| O-2 | The PR body template: its fields and its list of actions. Not defined yet |
| O-3 | Where the views live (page, stream view, session page). Decided at the unification of the features |

The spec in `docs/review-pages-spec.html` (tab F3) has the rules of today: PR states, blockers and `orch merge`.
