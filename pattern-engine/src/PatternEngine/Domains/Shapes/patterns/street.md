---
id: street
kind: group
level: 1
status: stable
phrase: a street
use_when:
  - The request names a street, a road or a row of two or more buildings or trees.
do_not_use_when:
  - when: The request names only one building or one tree.
    use: house
variants:
  - id: row
    when: Always. The objects stand in one row on the ground.
parts:
  - { ref: ground, count: 1 }
  - { ref: house, count: "0..n" }
  - { ref: tower, count: "0..n" }
  - { ref: tree, count: "0..n" }
  - { ref: shed, count: "0..n" }
  - { ref: sign, count: "0..n" }
related: [house, tower, tree, shed, sign]
asserts:
  - count(house,tower,tree,shed,sign) >= 2
  - all house.bottom == ground.top
  - all tower.bottom == ground.top
  - all tree.bottom == ground.top
  - all shed.bottom == ground.top
  - all sign.bottom == ground.top
  - no-overlap house tower tree shed sign
---
## Intent
Two or more buildings or trees in one row, on one ground line.

## Rules
- Do: put the ground part first, as a wide flat rectangle near the bottom of the canvas.
- Do: put each object on the ground, with its bottom on the ground top.
- Do not: let two objects overlap.

## Rationale
A shared ground line makes the objects read as one street.
