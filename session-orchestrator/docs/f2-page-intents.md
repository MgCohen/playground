# F2 Workflows: the intent of each page

Status: the intent pass is complete (2026-11-24). It comes before sketch round 5. Each page has a goal, its content, and the open points. Round 5 draws each open point as a variation.

## Rules for all pages

- Phone first. Keep each screen simple and clean.
- Each row shows little data. A tap opens the detail.
- Live pages (run, step run) show what happened. Configuration pages (workflow definition, step definition) show what is expected.
- An input or an output is a template document. A configuration page opens the template. A live page opens the actual document.

## Pages

| Page | Goal | Content | Open points for round 5 |
|---|---|---|---|
| W-1 Workflows (confirmed) | A gallery of the workflows that exist: see what each one does, and pick one to start. The same gallery is the picker that another button opens | One row for each workflow: the name and a one-line description. A tap opens W-2. No active runs: a workflow always runs inside a session | None |
| W-2 Workflow definition (confirmed) | See what a workflow does, before a start or during a run | Header: name, description, input, output (a tap opens the template). The flow graph in the run page shape, with no runtime data. Each row: the step name, its action (quick operation, agent task, start workflow ▸) and its ways out. A tap on a step opens a sheet: action, input, output, a link to W-2a, and for start workflow ▸ a link to the W-2 of the picked workflow. A "Start" button when the page opens from the picker | None |
| W-2a Step definition (confirmed for the sketch stage) | See the configuration of one step | Header: step name, owner workflow, action (quick operation, agent task, start workflow ▸). Body in this order: input template, output template, what it does (prompt, operation settings, or the picked workflow with a link to its W-2), ways out (each result and its next step), restart settings (reason and limit, only if the step can restart). A long prompt shows its first lines; a tap opens the full text. No runs, state or time | See open item O-1 |
| ~~W-3 Runs~~ (removed) | The session views show the runs. A run belongs to a session | The session views get the run row (W-C1) and a link to W-4 | None |
| W-4 Run (confirmed) | See where one run is, where it can go, and what happened | Header: workflow name, owner session, state, elapsed time; in a child level a breadcrumb (for example "Feature delivery › Verify ▸"). A small Next bar with the ways out of the current step. The graph on the left and the step rows on the right, oldest at the top; each row: step name, state icon, short result. A marker on the current step. A child flow ▸ row opens its level. A restart ↺ is a chip with its reason; the new pass continues in the same lane; old passes fold into one row. A tap on a row or a chip opens W-5. The wait card (W-C2) shows above the rows when the run waits for you. No prompts, templates or configuration | None |
| W-5 Step run sheet (confirmed) | Quick data about one step run, without a page change | A bottom sheet over the dimmed run page; the tree does not move. Step name, type, state (done, now, not run), time, short result. If the step ran: links to the actual input and output documents. Child flow ▸: an Open link to its level. Restart chip: the reason and the count against the limit. A link to W-2a. When the step waits for you: the same act buttons as the wait card (approve, change, answer). The rare control acts (force next, stop) are in a ⋯ menu in the run page header | None |
| W-C1 Run row (confirmed) | One line that shows a run on the session views | Workflow name, current step with the level path (for example "Verify ▸ Review"), a state dot, elapsed time, a "you" marker when the run waits for you. A tap opens W-4 | Round 5 draws one step bar try, as a variation only (Q-L2) |
| W-C2 Wait card (confirmed for now) | Show what a waiting run needs from you | The step, the question or the action, how long it waits, the act buttons (the ways out). It shows at the top of W-4 and on the page of the owner session. In the inbox, a wait is a normal question card with a link to the run (no new card kind) | None |
| W-6 Workflow editor (later) | Build a workflow | The input, the output, the steps and the connections. Steps belong to the workflow (decision Q-D1) | Not in round 5 |
| W-7 New session with a workflow (later) | Start a run from the UI | A workflow field on the new session form | Not in round 5 |

## Open items for the final shape

- **O-1: conditions.** Where do the conditions live, and how does a path know which way out to take (the rule that selects one result or another)? Do not solve it in the sketch stage. Source: the user, in the answer to Q-I4.

## Decided in the review of 2026-11-23

- W-4: the layout rule (graph left, rows right, marker, short header, sheet for details).
- W-5: the content of the sheet; links to the actual input and output documents.
- Two kinds of definition: the workflow definition and the step definition. A child flow is a step with the action "start workflow".
- Q-D1: steps belong to their workflow in version 1. Store design: issue #80.
- Q-D2: W-2 uses the run page shape, with no runtime data.
- W-1: a gallery and picker; runs belong to sessions. This replaces Q-L1.
- Q-I2: W-3 Runs is removed.
- Q-I3: the W-2 intent is confirmed.
- Q-I4: the W-2a intent is confirmed for the sketch stage. Open item O-1.
- Q-I5: the W-4 intent is confirmed.
- Q-I6: the W-5 intent is confirmed.
- Q-I7: the W-C1 intent is confirmed; the W-C2 intent is confirmed for now.
- The intent pass is complete. Next: sketch round 5.
- Q-L2: option 2 now (time and "you" marker). Option 3 (step bar) if a bar can show hubs, restarts and levels.
