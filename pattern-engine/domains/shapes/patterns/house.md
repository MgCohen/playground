---
id: house
kind: object
level: 2
status: stable
phrase: a house
use_when:
  - The request names a house or a home.
do_not_use_when:
  - when: The building is tall, narrow, or taller than it is wide.
    use: tower
  - when: The building is for tools, storage, bikes or a car.
    use: shed
variants:
  - id: plain
    when: No chimney is named.
  - id: chimney
    when: The request names a chimney or smoke.
    phrase: with a chimney
parts:
  - { ref: body, count: 1 }
  - { ref: roof, count: 1 }
  - { ref: door, count: 1 }
  - { ref: window, count: "0..4" }
  - { ref: chimney, count: 1, only: chimney }
params:
  door_fill: { part: door, attr: fill, default: brown, phrase: "with a {value} door", values: [red, blue, green, yellow] }
related: [street, tower, shed]
asserts:
  - body.height <= body.width
  - roof.bottom == body.top
  - roof.width >= body.width
  - door.bottom == body.bottom
  - door inside body
  - all window inside body
  - no-overlap door window
  - chimney.bottom >= roof.top
  - chimney.bottom <= roof.bottom
---
## Intent
One house, seen from the front: a body, a triangle roof and a door.

## Rules
- Do: make the body wider than it is tall.
- Do: make the roof base as wide as the body, or wider, and put it on the body top.
- Do: put the door on the bottom edge of the body.
- Do not: put the chimney fully above the roof. Its bottom is inside the roof.

## Rationale
A child reads this shape as "house" at once.
