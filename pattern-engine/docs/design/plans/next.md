# Next tasks

Open tasks after the decider and the grader moved into the topology engine (PR #16). Read the "Decided" sections of `decider.md`, `solution-grade.md` and `composition.md` first.

| # | Task | Done when |
|---|---|---|
| 1 | Make the text of the case `hand-partialfit-001` clear | One Haiku run (k=1) of the case has the expected ports |
| 2 | Fix `pe trust topology` on `retry-new-block.json` (flow) | `pe trust topology` passes |
| 3 | Do a fair lore test for party rung 6 | Three results side by side: new lore, old lore, lore off |

## 1. The case text

The decomposer reads "tries a failed payment again" as input Event and output Effect. The case expects Request to Event. The case author owns the expected types, so the text of the case changes, for example to "a request to pay". Do not change a prompt or a rules file to make one case pass.

## 2. The trust test

PR #16 did not change the grader and fixture code of this test. `pe trust` uses no model.

## 3. The fair lore test

The author of `lore.md` could see the hidden rubrics of the prose cases. "Lore on 4 of 4" shows only that the decomposer uses the lore. It does not show that the lore is general.

1. An agent writes a new lore file. It sees only the hero catalog and the generic decomposer rules. It must not read the cases, the anchors or the current lore file.
2. The old file stays as the baseline. A setting selects the lore file.
3. Run the prose cases p1 to p4 with Haiku, k=1: new lore, old lore, lore off.

## Limits

- Small Haiku runs only. A larger run only after the owner agrees.
- Deferred: roles as a setting, the shapes domain, and learning from won or failed quests.

## Design pages

| Page | Link |
|---|---|
| Engine design | https://claude.ai/artifact/J6GKDVMJHgm7NutCcFz2pS |
| Open decisions (all accepted) | https://claude.ai/artifact/G1k4PuxDb6nY3eo38MTjNv |
| Composition plan | https://claude.ai/artifact/SJY5dD2DQLZx5b4MxtH7Rt |
| Decider Map | https://claude.ai/artifact/QmgUexiTLK3c51eT1EzFE3 |
| Grader Map | https://claude.ai/artifact/9hmmqoyg57W9fH8unVdhsi |
