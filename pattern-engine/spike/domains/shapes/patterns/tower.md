---
id: tower
kind: object
level: 2
status: stable
phrase: a tower
use_when:
  - The request names a tower, or a tall or narrow building.
do_not_use_when:
  - when: The building is low and wider than it is tall.
    use: house
  - when: The tower holds a light, a lamp or sails, for example a lighthouse or a windmill.
    use: no-fit
variants:
  - id: plain
    when: Always.
parts:
  - { ref: body, count: 1 }
  - { ref: roof, count: 1 }
  - { ref: door, count: 1 }
  - { ref: window, count: "0..6" }
params:
  door_fill: { part: door, attr: fill, default: brown, phrase: "with a {value} door", values: [red, blue, green, yellow] }
related: [street, house]
asserts:
  - body.height >= body.width * 1.5
  - roof.bottom == body.top
  - roof.width >= body.width
  - door.bottom == body.bottom
  - door inside body
  - all window inside body
  - no-overlap door window
---
## Intent
One tall, narrow building with a pointed roof.

## Rules
- Do: make the body at least 1.5 times as tall as it is wide.
- Do: put the windows one above the other.
- Do not: make the roof narrower than the body.

## Rationale
The height is what makes a tower different from a house.
