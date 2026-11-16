---
id: handler
kind: leaf
level: 3
status: stable
primitive: component
inputs:
  - { name: command, type: Command }
outputs:
  - { name: event, type: Event }
---
## Intent
Runs a command and gives the event that it caused.
