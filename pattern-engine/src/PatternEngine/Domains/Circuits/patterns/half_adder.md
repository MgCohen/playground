---
id: half_adder
kind: object
level: 2
status: stable
phrase: a half adder
use_when:
  - The request adds two bits, with no carry in.
do_not_use_when:
  - when: The request also adds a carry in, or adds three bits.
    use: full_adder
variants:
  - id: plain
    when: Always.
parts:
  - { ref: xor, count: 1 }
  - { ref: and, count: 1 }
inputs:
  - { name: a, type: bit }
  - { name: b, type: bit }
outputs:
  - { name: sum, type: bit }
  - { name: carry, type: bit }
related: [full_adder]
---
## Intent
Adds two bits: sum = a XOR b, carry = a AND b.

## Rules
- Do: wire a and b to both gates.
- Do: drive sum from the XOR gate and carry from the AND gate.

## Rationale
The smallest adder. A full adder is made of two of them.
