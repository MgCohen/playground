---
id: mux2
kind: object
level: 2
status: stable
phrase: a two-input multiplexer
use_when:
  - The request selects one of two bits with a select bit.
variants:
  - id: plain
    when: Always.
parts:
  - { ref: not, count: 1 }
  - { ref: and, count: 2 }
  - { ref: or, count: 1 }
inputs:
  - { name: a, type: bit }
  - { name: b, type: bit }
  - { name: sel, type: bit }
outputs:
  - { name: y, type: bit }
---
## Intent
y = a when sel is 0, and y = b when sel is 1.

## Rules
- Do: AND a with NOT sel, AND b with sel, and OR the two results.

## Rationale
The basic selector of digital logic.
