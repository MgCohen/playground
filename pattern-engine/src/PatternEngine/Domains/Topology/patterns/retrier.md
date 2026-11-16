---
id: retrier
kind: leaf
level: 3
status: stable
primitive: component
inputs:
  - { name: event, type: Event }
outputs:
  - { name: command, type: Command }
---
## Intent
Takes a failed event and sends the command again: the back wire of a retry loop.
