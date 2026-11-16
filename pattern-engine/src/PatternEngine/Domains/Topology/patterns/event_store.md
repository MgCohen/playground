---
id: event_store
kind: leaf
level: 3
status: stable
primitive: component
inputs:
  - { name: event, type: Event }
outputs:
  - { name: saved, type: Saved }
---
## Intent
Saves an event.
