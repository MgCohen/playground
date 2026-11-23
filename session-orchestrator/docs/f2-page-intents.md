# F2 Workflows: the intent of each page

Status: the intent pass, one page at a time, with the user (2026-11-23). It comes before sketch round 5. Each page has a goal, its content, and the open points. Round 5 draws each open point as a variation.

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
| W-4 Run | See where one run is, and where it can go | Header: run name, state, elapsed time. The graph on the left and the step rows on the right, with a marker on the current step. One screen for each level. A child flow row opens its level | Header content. The look of a restart chip and of folded old passes. Where the "next" ways out show |
| W-5 Step run sheet | Quick data about one step run, without a page change | Type, state (done, now, not run), result. Links to the actual input and output documents. For a child flow: "Open" to its level. A link to W-2a | The actions on the sheet (answer, approve, force next) for a wait |
| W-C1 Run row | One line that shows a run on other pages | Run name, current step (with the level path), state, elapsed time, a "you" marker when it waits for you | A step bar (wanted, if it can show hubs, restarts and levels). See Q-L2 |
| W-C2 Wait card | Show what a waiting run needs from you | The step, the question or the action, the time it waits, the ways out | Where it shows: on the run page only, or on the session page and in the inbox too |
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
- Q-L2: option 2 now (time and "you" marker). Option 3 (step bar) if a bar can show hubs, restarts and levels.
