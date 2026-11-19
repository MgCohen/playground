---
name: visual-issue
description: Report a visual issue of the tray or the web UI in the issue tracker (docs/tray-issues.html), with proof, and with no fix. Use it when the user or a test finds a visual problem - a cut, a blink, a wrong size, a wrong motion, a test gap - or when the user says "track this" or "add it to the tracker".
---

# Report a visual issue

The tracker is `docs/tray-issues.html` (https://claude.ai/artifact/KcP1KAskQiQqiWALAAF87s). It has one page for each issue. The user picks which issue we work on.

## Rules

1. Write a record. Do not fix the issue, also when the fix looks small.
2. Try to reproduce it one time at most.
3. If the result or the error is clear, add the proof. If it is not clear after one try, write what you tried, and stop.
4. Do not test causes one after the other. One cause can make many issues, and one issue can have many causes. The user decides when to look for causes.
5. Look at the frames before you trust a number. A frame counter can give a false alarm (issue I1).
6. Never use the real pointer of the user. The test runs click with window messages.

## Steps

1. Read the overview page of the tracker. Look for an issue with the same cause or the same place. If one exists, add your data to that page.
2. Take the next free ID: `I<n>` for an open issue. A fixed issue keeps its ID.
3. Copy the page of an issue with the same status, and change the content. Write each field of the template.
4. Add a line to the page list (`<option>`) and a row to the table of all issues.
5. Put each proof picture in `docs/screens/tray-redesign/issues/`. Publish the page with the picture in `files` (`issues/<name>.jpg`).
6. If the issue has a question for the user, add a row to the "Open questions" page. Publish the tracker to the same URL. Record the issue in `docs/plan.md`. Commit.
7. Tell the user the ID, the status and the question. End the turn.

## Template

| Field | Content |
|---|---|
| Status | Reported, Detected, Replicated, Fixed or Not real (see the status flow on the overview page) |
| Source | The user, the self-test, a stress run, a demo, a review |
| What | One sentence: what is wrong, and where |
| Proof | A picture, a video or data, with the run name. If none: "None yet" |
| Replicate | The command, and the number that it gives |
| Cause | Known, with the data. Or not known, with what you tried |
| Proposal | The fix options, and the check that proves the fix |
| Decision | One question for the user |

## Proof tools

| Need | Tool |
|---|---|
| A recording of the screen, with a time and a mean value for each frame | `spikes/tray-display/recorder`: `recorder x y width height scale fps seconds out.gif` |
| An MP4 for the user | `recorder mp4 in.gif out.mp4 30` |
| The frames at given times, side by side | `spikes/tray-display/tools/at.ps1 -Gif -Times -Out -At "HH:mm:ss.fff,..."` |
| The five frames around the nth empty frame | `spikes/tray-display/tools/strip.ps1 -Gif -Times -Out -Nth n` |
| A full run of the tray | `Orchestrator.Tray --selftest <folder> --demo` (log in `demo.txt`) |
| A check with a number | A new `Add(...)` in `src/Orchestrator.Tray/SelfTest.cs`. It may fail: that is the proof |

The test guide (`docs/tray-testing.html`) explains how the tests work.

## Later

The records can move to a database (the artifact `db` capability) with one form for a new issue. Until then, the HTML page is the store.
