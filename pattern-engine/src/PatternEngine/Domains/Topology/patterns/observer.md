---
id: observer
kind: object
level: 2
status: stable
phrase: an observer
use_when:
  - Other parts react to an event, each on its own, for example to send an email or update a report.
variants:
  - id: plain
    when: Always.
parts:
  - { ref: subscriber, count: "1..3" }
inputs:
  - { name: event, type: Event }
outputs:
  - { name: effect, type: Effect }
related: [command_pipeline, cqrs_system]
---
## Intent
One event goes to one or more subscribers; each reacts on its own.

## Rules
- Do: wire the event to each subscriber.
- Do: give the effect of the first subscriber as the output.

## Rationale
The part that causes an event does not know who reacts to it.
