---
id: query_handler
kind: object
level: 2
status: stable
phrase: a query handler
use_when:
  - The request reads data and returns a view of it.
do_not_use_when:
  - when: The request also changes or saves data.
    use: command_pipeline
variants:
  - id: plain
    when: Always.
parts:
  - { ref: query_endpoint, count: 1 }
  - { ref: reader, count: 1 }
inputs:
  - { name: request, type: Request }
outputs:
  - { name: view, type: View }
related: [command_pipeline, cqrs_system]
---
## Intent
A request becomes a query, and a reader gives a view: the read side.

## Rules
- Do: never change data in a query handler.

## Rationale
Reads and writes change for different reasons, so they stay apart.
