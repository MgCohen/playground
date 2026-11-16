---
name: orch-ask
description: How a session under the orchestrator asks the user a question with mcp__orch__ask_user (the decision template). Use it before each question to the user, and when you must decide if a question is necessary.
---

# Ask the user

The user answers in free text or by voice, often from the phone. A good question gets an answer in one read.

## When to ask

| Case | Do |
|---|---|
| The brief, `AGENTS.md`, `CLAUDE.md`, `README.md` or `docs/` answer it | Do not ask. Follow the docs |
| Easy to undo and local to the task | Decide yourself and say it in the report |
| A design, an API, a file layout or a convention | Ask, with a default option |
| Hard to undo, or an effect outside the repository | Ask, risk at the strictest level, and no default if a wrong default is dangerous |

## Fill the decision template

`orch template decision` shows the fields. Each call asks one question.

| Field | Content |
|---|---|
| `question` | One question, one sentence |
| `context` | Why the question comes up now, and what the user must know |
| `snippet` | Optional: the code or text that shows the problem |
| `options` | Two to four suggestions. The user can answer something else |
| `recommendation` | Your pick and its reason |
| `risk` | A policy level. The tool description gives the definition of each level |
| `defaultOption` | The option that applies if the user does not answer in time |
| `waitSeconds` | 0 for most questions: the tool returns PENDING and you end your turn |

## After the call

1. If the result is PENDING, end your turn. Do not continue with a guess.
2. The answer arrives as a new message: "Answer to decision <id> (<question>): <text>".
3. If the answer has more instructions (a default decision), do each step in order. The skill `orch:orch-stack` explains the steps.
4. If the answer is not one of the options, follow the answer and say so in your next report.
