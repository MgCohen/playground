# Round 5 brief: all F2 pages after the intent pass, phone, Git tree style

You draw mobile UI sketches (SVG wireframes with real sample data) for the Workflows feature (F2) of a session orchestrator. The user confirmed the intent of each page. Round 5 draws each page, and each open point as a variation.

## Read first
- `session-orchestrator\docs\f2-page-intents.md`: THE SOURCE. The goal and the content of each page, and the rules for all pages. Follow it exactly.
- The last rows (2026-11-23) of `...\session-orchestrator\docs\decisions.md`: the decisions behind it.
- Scratchpad folder `<scratchpad>\`:
  - `brief.md`: the colors (light and dark) and the SVG rules. Its run data is OLD.
  - `brief4.md`: the P5 model, and the W4 "Feature delivery" run #14 and W3 "Planning" run #7 data. USE THIS DATA.
  - `r4w4/`, `r4def/`, `r4list/`: the round 4 generators. Copy what you need into your own folder (`r5a/` or `r5b/`, see your part). Do not change the originals.
- The round 4 SVGs in `...\session-orchestrator\docs\run-views\r4\` (look at the PNGs in the r4 generator folders): the style the user accepted as the skeleton. Keep the header, the rows, the lanes, the chips, the sheet, the fonts and sizes.

## Style rules (from the user)
- Simple and clean. Little data on each row. A tap opens the detail. Remove each part that is not necessary to read the state.
- Run page: the graph on the left, the step rows on the right, a marker on the current step, a short header on top.
- Live pages (run, step run) show what happened and link the ACTUAL input and output documents. Configuration pages (workflow definition, step definition) show what is expected and link the TEMPLATES.
- A child flow ▸ is a step with the action "start workflow". Step actions: quick operation, agent task, start workflow ▸.
- Runs belong to sessions. There is no global runs page.

## Part A (folder `r5a/`, JSON `r5-def.json`): W-1, W-2, W-2a
- W-1 Workflows gallery. a: the gallery (5 to 6 workflows: Feature delivery, Planning, Incident triage, PR review, Verify, Question round), name and one-line description, a search field. b: the same gallery as a picker, opened from the new session form ("Pick a workflow", a selected row). c: an empty or search state, or a variation with a small action-kind hint; your choice, say it in the caption.
- W-2 Workflow definition (Feature delivery). a: the page: header with name, description, input and output (tap opens the template), the flow graph in the run page shape with no runtime data; each row: step name, action, ways out. b: a tap on "Verify ▸" opens a sheet: action start workflow ▸, input, output, links "Step definition" and "Open Verify workflow". c: the page opened from the picker, with a "Start" button.
- W-2a Step definition. a: agent task "Review" or "Write spec": header (step name, owner workflow, action), then input template, output template, what it does (the first lines of the prompt, "Show all"), ways out. b: start workflow ▸ "Verify": the picked workflow with a link to its W-2, and how the parent maps each exit (approved → Release, design wrong → restart ↺ change, limit → exit Escalate). c: quick operation or agent step "Fix" with restart settings (reason error and change, limit 3 each).

## Part B (folder `r5b/`, JSON `r5-run.json`): W-4, W-5, W-C1, W-C2
- W-4 Run. a: W4 level 1 with the wait card on top (the run waits in Verify ▸ Review). Header: workflow name, owner session, state, elapsed time, ⋯ menu. A small Next bar. b: level 2 "Verify ▸" screen, breadcrumb "Feature delivery › Verify ▸", pass 1 folded into one row, the chip "↺ pass 2 · error · 2 tests failed", pass 2 rows, marker on Review. c: W3 Planning, level 2 "Question round ▸", passes 1 and 2 folded, pass 3 at Answer.
- W-5 Step run sheet. a: a done step (Test, pass 1): type, state, time, result, links "Input: ..." and "Output: test-report.md" to the actual documents, link "Step definition". b: the waiting step Review: the same act buttons as the wait card (approve, change, design wrong). c: a tap on the restart chip: reason error, count 1 of 3, what step 1 sees.
- W-C1 Run row. a: on a session page (the session "export-button" runs Feature delivery #14). b: in a session list, several sessions, two with a run row, one with the "you" marker. c: the step bar TRY: it must work for a hub flow that goes back and forth, restarts and levels (for example one segment per level-1 step, a ↺ count, and the child level shown inside its segment). Caption it as a try.
- W-C2 Wait card. a: on top of the run page (zoom in on the card, or the full screen). b: on the page of the owner session. c: in the inbox, as a normal question card with a link "Open run".

## Output contract
- Write into `session-orchestrator\docs\run-views\r5\` (make the folder if it does not exist). Only your files: Part A names `gal-a|b|c.svg`, `wdef-a|b|c.svg`, `sdef-a|b|c.svg`; Part B names `run-a|b|c.svg`, `sheet-a|b|c.svg`, `row-a|b|c.svg`, `wait-a|b|c.svg`. Plus your JSON file.
- Each SVG: `viewBox="0 0 360 720" width="360" height="720"`, self-contained, inline `<style>` with the light and dark colors, no scripts, no external files. Min font size 9. Text stays in its box.
- JSON: `{"rows": [{"id": "<letters and digits, max 10>", "name": "<page code and name, for example W-4 · Run>", "why": "max 50 words, simple English", "sketches": [{"file": "...svg", "caption": "max 36 chars"}, ...]}]}`. 3 sketches in each row. Row ids: Part A `r5gal`, `r5wdef`, `r5sdef`; Part B `r5run`, `r5sheet`, `r5row`, `r5wait`.
- Check that each SVG parses, then render-check each one with headless Edge (see `tree2/render.sh` in the scratchpad; use your own folder for profiles and PNGs). Fix overlaps. Compute positions in a script.
- Simple, short English in all labels. Use the terms: workflow, flow, level, step, step run, action, child flow ▸, start workflow, pass, exit, restart ↺, reason, split, join, way out, input, output, template.
- Do NOT edit other files. Do NOT run git. Do NOT start servers. Do NOT touch the orchestrator store or port 7777.
- Final reply: the files, one line each, under 120 words.
