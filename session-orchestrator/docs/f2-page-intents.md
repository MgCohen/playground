# F2 Workflows: the intent of each page

Status: draft for review (2026-11-23). This is the high-level pass before sketch round 5. Each page has a goal, its content, and the open points. Round 5 draws each open point as a variation.

## Rules for all pages

- Phone first. Keep each screen simple and clean.
- Each row shows little data. A tap opens the detail.
- Live pages (run, step run) show what happened. Configuration pages (workflow definition, step definition) show what is expected.
- An input or an output is a template document. A configuration page opens the template. A live page opens the actual document.

## Pages

| Page | Goal | Content | Open points for round 5 |
|---|---|---|---|
| W-1 Workflows | Find a workflow and its runs | One row for each workflow: the name, the number of active runs, the time of the last run. A tap opens W-2. A second target opens W-3 for that workflow | Sort order (active first, or by name). Show the input and output kind on the row, or not |
| W-2 Workflow definition | See what a workflow does, with no run data | Header: name, input, output. The flow graph in the run page shape. A tap on the input or the output opens its template. A tap on a step opens a sheet with a link to W-2a | What the step sheet shows. Where the list of runs of this workflow goes (a link, or a short section) |
| W-2a Step definition | See the configuration of one step | The input and output templates, the action (quick operation, agent task, start workflow), the instructions, prompts or script. For "start workflow": the picked workflow, with a link to its W-2 | The order of the parts. How a long prompt shows on a phone (folded, or a full page) |
| W-3 Runs | See all runs, and which runs need you | One run row (W-C1) for each run. Filters: state, workflow | The filters. Group by state, or one list |
| W-4 Run | See where one run is, and where it can go | Header: run name, state, elapsed time. The graph on the left and the step rows on the right, with a marker on the current step. One screen for each level. A child flow row opens its level | Header content. The look of a restart chip and of folded old passes. Where the "next" ways out show |
| W-5 Step run sheet | Quick data about one step run, without a page change | Type, state (done, now, not run), result. Links to the actual input and output documents. For a child flow: "Open" to its level. A link to W-2a | The actions on the sheet (answer, approve, force next) for a wait |
| W-C1 Run row | One line that shows a run on other pages | Run name, current step (with the level path), state, elapsed time, a "you" marker when it waits for you | A step bar (wanted, if it can show hubs, restarts and levels). See Q-L2 |
| W-C2 Wait card | Show what a waiting run needs from you | The step, the question or the action, the time it waits, the ways out | Where it shows: on the run page only, or on the session page and in the inbox too |
| W-6 Workflow editor (later) | Build a workflow | The input, the output, the steps and the connections. Steps belong to the workflow (decision Q-D1) | Not in round 5 |
| W-7 New session with a workflow (later) | Start a run from the UI | A workflow field on the new session form | Not in round 5 |

## Decided in the review of 2026-11-23

- W-4: the layout rule (graph left, rows right, marker, short header, sheet for details).
- W-5: the content of the sheet; links to the actual input and output documents.
- Two kinds of definition: the workflow definition and the step definition. A child flow is a step with the action "start workflow".
- Q-D1: steps belong to their workflow in version 1. Store design: issue #80.
- Q-D2: W-2 uses the run page shape, with no runtime data.
- Q-L1: the W-1 row.
- Q-L2: option 2 now (time and "you" marker). Option 3 (step bar) if a bar can show hubs, restarts and levels.
