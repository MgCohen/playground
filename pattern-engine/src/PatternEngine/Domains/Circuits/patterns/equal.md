---
id: equal
kind: object
level: 2
status: stable
phrase: a one-bit equality checker
use_when:
  - The request checks if two bits are the same.
variants:
  - id: plain
    when: Always.
parts:
  - { ref: xor, count: 1 }
  - { ref: not, count: 1 }
inputs:
  - { name: a, type: bit }
  - { name: b, type: bit }
outputs:
  - { name: y, type: bit }
---
## Intent
y = 1 when a and b are the same: NOT (a XOR b).

## Rules
- Do: invert the output of the XOR gate.

## Rationale
The smallest comparator.
