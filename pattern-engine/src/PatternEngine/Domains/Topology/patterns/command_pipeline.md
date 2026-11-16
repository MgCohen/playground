---
id: command_pipeline
kind: object
level: 2
status: stable
phrase: a command pipeline
use_when:
  - The request changes data when a user or a system sends a request, in steps.
do_not_use_when:
  - when: The request only reads data and changes nothing.
    use: query_handler
variants:
  - id: plain
    when: No check of the input is named.
  - id: validated
    when: The request asks to check or validate the input first.
    phrase: that validates its input
parts:
  - { ref: endpoint, count: 1 }
  - { ref: validator, count: 1, only: validated }
  - { ref: handler, count: 1 }
  - { ref: event_store, count: 1 }
inputs:
  - { name: request, type: Request }
outputs:
  - { name: event, type: Event }
related: [query_handler, observer, cqrs_system]
---
## Intent
A request becomes a command, the handler runs it, and the event is saved: endpoint, (validator), handler, event store.

## Rules
- Do: keep one step for each component, in this order.
- Do: give the saved event as the output, so other parts can react to it.

## Rationale
The command pattern with a pipeline: each step is small and can change alone.
