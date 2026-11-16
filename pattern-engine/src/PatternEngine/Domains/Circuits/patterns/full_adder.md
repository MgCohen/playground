---
id: full_adder
kind: object
level: 1
status: stable
phrase: a full adder
use_when:
  - The request adds two bits and a carry in, or adds three bits.
do_not_use_when:
  - when: There is no carry in and only two bits.
    use: half_adder
variants:
  - id: plain
    when: Always.
parts:
  - { ref: half_adder, count: 2 }
  - { ref: or, count: 1 }
inputs:
  - { name: a, type: bit }
  - { name: b, type: bit }
  - { name: cin, type: bit }
outputs:
  - { name: sum, type: bit }
  - { name: cout, type: bit }
related: [half_adder]
---
## Intent
Adds a, b and cin: two half adders and an OR gate for the carry out.

## Rules
- Do: add a and b in the first half adder, then add its sum and cin in the second.
- Do: drive cout from an OR of the two carries.

## Rationale
The building block of a ripple adder.
