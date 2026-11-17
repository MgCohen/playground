---
name: orch-report
description: When and how a session under the orchestrator sends a report (mcp__orch__report) or an update (mcp__orch__update). Use it at the end of each step, when the work is blocked, and when it is done.
---

# Report and update

| Tool | Template | Reader must act | Use |
|---|---|---|---|
| `mcp__orch__update` | update | No | A step starts or ends, and the work continues |
| `mcp__orch__report` with `working` | report | No | A branch or a PR is ready for a later review |
| `mcp__orch__report` with `blocked` | report | Yes | You cannot continue without the user or the parent |
| `mcp__orch__report` with `done` | report | Yes | The brief is done. The parent gets the report as a message |

## Fill the fields

| Field | Content |
|---|---|
| `summary` | One or two sentences: the result first |
| `done` | The finished parts, one item each |
| `next` | The next steps |
| `blockers` | What stops the work, and what you need |
| `links` | Branches, PR links and files |

## Rules

- Send `done` only when the "done when" check of the brief passes.
- The end of a step is an update, not a report. The report tool refuses `done` while you have an open decision.
- The supervisor sends your report to your parent at the end of your turn. If you send two reports in one turn, the parent gets only the last one.
- If your turn ends with no final report, the supervisor sends you a "no final report" notice after some time. Then report `done` or `blocked`, or continue the work.
- Send one report for each state change. Do not repeat a report with no change.
- After `done` or `blocked`, end your turn.
