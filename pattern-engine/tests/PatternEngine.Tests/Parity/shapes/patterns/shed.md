---
id: shed
kind: object
level: 2
status: stable
phrase: a shed
use_when:
  - The request names a shed, a garage or a tool hut.
do_not_use_when:
  - when: People live in the building.
    use: house
variants:
  - id: plain
    when: Always.
parts:
  - { ref: body, count: 1 }
  - { ref: roof, count: 1 }
  - { ref: door, count: 1 }
params:
  door_fill: { part: door, attr: fill, default: brown, phrase: "with a {value} door", values: [red, blue, green, yellow] }
related: [house, street]
asserts:
  - body.height <= body.width
  - roof.bottom == body.top
  - roof.width >= body.width
  - door.bottom == body.bottom
  - door inside body
  - door.width >= body.width * 0.4
---
## Intent
A small building for tools, bikes or a car. It has a wide door and no windows.

## Rules
- Do: make the door at least 0.4 times as wide as the body.
- Do not: add windows.

## Rationale
The wide door and the missing windows show that nobody lives in it.
