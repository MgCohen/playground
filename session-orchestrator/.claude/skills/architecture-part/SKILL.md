---
name: architecture-part
description: Write or update one part tab of docs/ui-architecture.html (for example Motion, Tracing, Style). Use it when you rate a part of the view, add a part tab, or change the review, the code, the call stacks or the requirements of a part tab.
---

# Architecture part tab

A part tab shows one part of the view: its review, how it works, its code, its API and its requirements. All part tabs use the same format.

## Which tabs use this template

Only part tabs: one part of the view, with code that you can review (for example Motion, Places, Tracing). Other tabs keep their own free format and do not follow this skill: the Overview, research (Prior art), high-level proposals, decision records, and context, explanation or question tabs.

## Start

1. Copy the Template tab of the page (`<div class="panel off" id="template">`).
2. Change the `id` to the part name. Add the tab button in `nav`, and add the `id` to the `names` list of the tab script.
3. Replace each `‹ ›` placeholder. Delete the HTML comments of the template.

## Steps

1. Read the code of the part first: the entry points, the internal units, the tests.
2. Collect real snippets. Copy each line from the source. Write the full file path.
3. Collect real trace lines or log lines. Run the part, or use a recent self-test run.
4. Rate the part: Good, Mixed or Weak (`span.q good|mixed|weak`).
5. Write one review item for each finding.
6. Count an issue only in the part that owns its cause. In the other parts, use a Dependency item.
7. Write one requirement item for each requirement, with its status and its design.

## Format checklist

- Header: the part name, the rating, and one summary sentence.
- Sections in this order, each a `details.sec`: Review, Architecture, Definition, Key code, Call stacks, API, Quick guide, Requirements.
- Each section starts with its instruction line (`p.summary`). Keep it, or make it specific to the part.
- The Review section is open. The first item of each section is open.
- Each item is a `details.example.f` with 2 header lines:
  - `.l1`: the title, then the tags at the right end.
  - `.l2`: a one-line subtitle.
  - On the left, put only an index ("G1 ·", "R1 ·") or one kind chip that is always true (API).
- Review labels: Good (`rs ok`), Gap (`rs no`), Dependency (`rs dep`), Trade-off (`rs part`). One label for each item.
- A Gap has a "To close it:" line. A Trade-off has a "Gain:" line and a "Cost:" line.
- Architecture: an Outside subsection and an Inside subsection. Each has one graph (`div.box.zoom > pre.mermaid`) and one text call tree.
  - Pick the graph type that matches the concept: a `sequenceDiagram` for calls and waits across runtimes, a topology (`flowchart`) for units and links, a flow (`flowchart` or `stateDiagram`) for decisions and states.
  - Check the graph against the code. Show each await, each runtime boundary and each result path that exists. Do not draw a helper call as a stage of a chain.
- Code snippets:
  - Real code from the source only. Never write invented code.
  - A note is a comment line above the code line. It starts with "← ".
  - The full file path is above each snippet (`p.file`).
  - Each snippet has a language class (`code.language-csharp`, `-css`, `-javascript`, `-xml`, `-plaintext`).
- Key code header: line 1 is the title and the language tags (`tag cs`, `tag css`, `tag js`, `tag`). Line 2 is the class or method names.
- Call stacks: each item has Context, Call stack and Output.
  - Call stack: a text tree (`├─`, `└─`, `│`). The entry point is first. Give `file:line`.
  - A result is a child line (`└─ result: …`). A note is a line with the tree prefix (`│  ← …`). No line comes in from the side.
  - Output: the real trace lines or log lines.
- API: only the main entry points and types that a caller uses.
- Requirements: Covered (`rs ok`), Partly (`rs part`), Not covered (`rs no`). Each has the detail and a "Design:" line with the test that checks it.
- If a section links to other documents, put the link in the last line. If no link, delete the placeholder line.
- No big tables. No disclaimer notes.
- Write all text in Simplified Technical English.

## Do not change the format

Change only the content. The page CSS and the template give the format. These rules are agreed with the user:
- Tags are at the right end of the header line. Only an index or one guaranteed kind goes on the left, so the titles start at the same place.
- A closed header is always 2 single lines (title, subtitle), cut with "…". An open header shows the full text. All closed headers have the same height.
- Keep text short: a title of a few words, a subtitle of one line, items of 2 or 3 sentences.
- Code fits its box: the font gets smaller (to 6 px). Do not break code lines by hand.
- Each section and each item is a foldable. No big tables. No disclaimer notes.
- A graph opens in a fitted view (click), with pinch zoom and a close button.

## Check before you publish

1. The HTML tags are balanced. The panel opens and closes once.
2. Each item header has 2 lines: `.l1` with `.t`, and `.l2`.
3. No `‹ ›` placeholder stays in the tab.
4. Each snippet is real code. Compare it with the source file.
5. Each trace line is real. Compare it with a run.
6. The page fits 375 px: no horizontal scroll of the page.
7. The flow graphs render, and the code has syntax colors.
