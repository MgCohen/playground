---
id: store
kind: object
level: 2
status: stable
phrase: a store
use_when:
  - The request keeps events or changes, so that they are saved.
variants:
  - id: plain
    when: Always.
parts:
  - { ref: event_store, count: 1 }
inputs:
  - { name: event, type: Event }
outputs:
  - { name: saved, type: Saved }
related: [command_pipeline, cqrs_system]
---
## Intent
An event goes in, and the store saves it.

## Rules
- Do: wire the event to the event store.
- Do: give the saved record as the output.

## Rationale
Saving is a job of its own. A command pipeline runs a command; a store keeps the result.
