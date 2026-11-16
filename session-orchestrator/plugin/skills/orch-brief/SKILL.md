---
name: orch-brief
description: How to start a child session with mcp__orch__spawn_child (the brief template), and the rules for child sessions. Use it before you split work into child sessions.
---

# Start a child session

A child knows only its brief. A short brief that names skills works better than a long brief that repeats them.

## Fill the brief template

`orch template brief` shows the fields.

| Field | Content |
|---|---|
| `title` | A short title that names all of the scope of the child |
| `goal` | What the child must make, and why. Put the steps here if the order matters |
| `scope` | What is in scope and what is not |
| `doneWhen` | The check that shows that the work is done |
| `skills` | The skills to load first, for example `orch-ask`, `orch-report`, `orch-stack` |
| `agent` | `worker` for one task, `orchestrator` for a child that splits work again |
| `cwd` | The working folder. Default: your folder |
| `worktree` | `true` if two children change one repository at the same time |

## Rules

- Give each child one decision group: one area of work, one owner of its questions.
- A child reports `done` or `blocked`. You get the report as a message that starts with "Report from child".
- After you start children and have nothing else to do, end your turn. Do not wait in a loop.
- Do not archive a child that waits for the user, or a child that has an open PR.
