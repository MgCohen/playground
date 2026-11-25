# Shared brief: run page sketches (SVG only)

You draw UI sketches (wireframes with real sample data) of the RUN PAGE of a workflow runner. A workflow is a graph of steps. A run is one execution of a workflow. The user wants a page that shows the WHOLE graph of a running workflow, with:
- the current step highlighted (style "now"),
- the possible next steps highlighted in a different style ("next"),
- done steps (passed / failed), and future steps (faded),
- loops (a step can run more than once; show the count or the passes),
- run data: run name, workflow name, elapsed time, state ("waits for you"), who acts now,
- a way to go back and forth (along the history, or along the graph),
- a tap on a node that shows the information of that step (its step runs, input, output, time, executor, where it went, the ways out).
The graph must be visible. It can be the whole page, a part of the page, or integrated in the page. If the graph is too big, it scrolls or pans.

## Output contract (follow exactly)
- Write only into the folder `session-orchestrator\docs\run-views\`.
- Write 3 sketches as standalone SVG files: `<concept>-a.svg`, `<concept>-b.svg`, `<concept>-c.svg` (`<concept>` is given in your task). All 3 sketches of your concept use the SAME frame size: either the side panel `viewBox="0 0 360 720"` or the wide screen `viewBox="0 0 960 600"` (your task says which). Set `width` and `height` attributes equal to the viewBox size.
- Each SVG is self-contained: inline `<style>` only, no scripts, no external images, no external fonts (use `font-family: Inter, system-ui, sans-serif`; mono: `ui-monospace, monospace`). Draw everything with SVG shapes and `<text>`. Text must not overflow its box; keep labels short. Minimum font size 9.
- Colors: define them as CSS classes in the inline `<style>`, with a light set and a dark set under `@media (prefers-color-scheme: dark)`. Light: page #fdfdfc, soft panel #f1eee9, text #2a2825, muted #6b665f, line rgba(27,26,24,.14), edge #8d877e, loop edge #c2410c (dashed), agent #1f5fbf, human #ff6a1a, tool #6b665f, passed #2f8f5b, failed #c23b22, NOW #7c4dff (solid ring 3px, soft fill rgba(124,77,255,.12)), NEXT #7c4dff dashed ring, selected (tapped) #1f5fbf ring. Dark: page #262522, soft #2e2c29, text #ece9e4, muted #a39d94, line rgba(255,255,255,.12), edge #8a847b, loop #ff8a4c, agent #6ea0ff, human #ff7a33, tool #a39d94, passed #5cc28a, failed #ff7a66, now #a98bff, selected #6ea0ff.
- Executor color is on the node (fill of a dot, or a left bar or stroke of a box). State is a ring, an icon or a chip.
- Also write `<concept>.json`: `{"name": "...", "tag": "side panel · vertical" (or similar, short), "why": "one paragraph, max 60 words, simple English: what the page is, how the graph is used, best for, weak for", "sketches": [{"file": "<concept>-a.svg", "caption": "max 40 chars"}, ...]}`.
- Check each SVG parses: `python -c "import xml.dom.minidom,sys; xml.dom.minidom.parse(sys.argv[1])" <file>`. Then render-check it if you can (optional). Fix overlaps you can see from your coordinates: compute positions, do not guess.
- Write texts in simple, short English (short sentences, active voice).
- Do NOT edit any other file. Do NOT run git. Do NOT start servers. Do NOT touch the orchestrator store or port 7777.
- Prefer a short Python script (in your own temp folder, not in the repo) that generates the SVGs from the data below, so positions are computed.
- Final reply: list the files and one line for each sketch. Under 120 words.

## Sketch set
- Sketch a: run W4 (Feature delivery) now, as the page first opens.
- Sketch b: the same page after a tap on the node "Test" (it ran 2 times), OR the second page/modal of your concept for that tap.
- Sketch c: run W3 (Planning) now, in the same concept (shows a loop that runs many times).

## Workflow W4: Feature delivery. Run #14 "Add export button". Elapsed 1h 23m. State: waits for you (Review).
Steps (id, label, executor): spec "Write spec" agent; split "Split tasks" agent; api "API task" agent; ui "UI task" agent; docs "Docs task" agent; integ "Integrate" tool; test "Test" tool; review "Review" human; fix "Fix" agent; release "Release" tool (end).
Edges: spec→split; split→api, split→ui, split→docs (parallel); api→integ, ui→integ, docs→integ (join); integ→test; test→review "pass"; test→fix "fail"; fix→test "re-test" (loop); review→fix "change" (loop); review→release "approve".
History (step runs, oldest first):
1 Write spec ✓ 4m "spec.md · 6 parts"
2 Split tasks ✓ 1m "3 tasks · 3 sessions"
3 API task ✓ 18m "+210 −12 · 4 files" (parallel)
4 UI task ✓ 25m "+340 −40 · 7 files" (parallel)
5 Docs task ✓ 6m "+80 · 2 files" (parallel)
6 Integrate ✓ 2m "3 branches merged"
7 Test pass 1 ✕ failed 3m "41 passed · 2 failed" → went to Fix
8 Fix ✓ 9m "2 tests fixed · +18 −6"
9 Test pass 2 ✓ 3m "43 passed" → went to Review
10 Review NOW — human (you), waits 12m. Input: Test pass 2, branch feat/export +628 −58, 13 files.
Next: approve → Release (then the run ends); change → Fix (fix pass 2, then Test pass 3).
Failed tests in pass 1: Export_EmptyList_ReturnsHeader, Export_Unicode_KeepsBytes.

## Workflow W3: Planning. Run #7 "Plan the workflow runner v2". Elapsed 38m. State: waits for you (Answer).
Steps: read "Read the goal" agent; ask "Ask a question" agent; answer "Answer" human; record "Record" agent; spec "Write the spec" agent; approve "Approve" human; propose "Propose tasks" agent (end).
Edges: read→ask; ask→answer; answer→record; record→ask "more" (loop); record→spec "done"; read→spec "no questions" (skip); spec→approve; approve→ask "change" (loop); approve→propose "approve".
History:
1 Read the goal ✓ 2m "goal.md · 3 open points"
2 Ask Q1 ✓ "Where does a run start?"  3 Answer ✓ 4m "From a session"  4 Record ✓ → more
5 Ask Q2 ✓ "Max loop count?"  6 Answer ✓ 3m "3, then ask"  7 Record ✓ → more
8 Ask Q3 ✓ "Start a run from the UI in v1?"
9 Answer NOW — human (you), waits 6m. Question round 3.
Next: Record (then more → Ask Q4, or done → Write the spec). Future: Write the spec, Approve, Propose tasks.
