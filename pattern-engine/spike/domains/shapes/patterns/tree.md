---
id: tree
kind: object
level: 2
status: stable
phrase: a tree
use_when:
  - The request names a tree.
variants:
  - id: plain
    when: Always.
parts:
  - { ref: trunk, count: 1 }
  - { ref: crown, count: 1 }
related: [street]
asserts:
  - crown.cx == trunk.cx
  - crown.bottom >= trunk.top
  - crown.bottom <= trunk.bottom
  - crown.width >= trunk.width
---
## Intent
One tree: a trunk with a round crown on top.

## Rules
- Do: center the crown on the trunk.
- Do: let the crown overlap the top of the trunk.

## Rationale
A circle on a rectangle reads as a tree.
