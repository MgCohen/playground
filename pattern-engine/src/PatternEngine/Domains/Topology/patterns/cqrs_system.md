---
id: cqrs_system
kind: group
level: 1
status: stable
phrase: a system
use_when:
  - The request needs two or more of these jobs - change data, read data, react to events.
do_not_use_when:
  - when: The request needs only one of these jobs.
    use: command_pipeline
variants:
  - id: plain
    when: Always.
parts:
  - { ref: command_pipeline, count: "0..1" }
  - { ref: query_handler, count: "0..1" }
  - { ref: observer, count: "0..2" }
related: [command_pipeline, query_handler, observer]
---
## Intent
A system of parts that each do one job. The events of the command pipeline go to each observer.

## Rules
- Do: wire the event output of the command pipeline to the event input of each observer.
- Do: keep the read side apart from the write side.

## Rationale
CQRS with events: each part can change and grow alone.
