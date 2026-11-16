---
id: sign
kind: object
level: 2
status: stable
phrase: a sign
use_when:
  - The request names a sign, a signboard or a notice board.
variants:
  - id: plain
    when: Always.
parts:
  - { ref: post, count: 1 }
  - { ref: board, count: 1 }
params:
  board_fill: { part: board, attr: fill, required: true, phrase: "with a {value} board", values: [red, blue, green, yellow] }
related: [street]
asserts:
  - board.cx == post.cx
  - board.width >= post.width * 3
  - board.bottom >= post.top
  - board.bottom <= post.bottom
---
## Intent
One sign: a board on top of a post.

## Rules
- Do: center the board on the post.
- Do: let the board overlap the top of the post.
- Do not: draw the sign without a board color. The request must give it.

## Rationale
The board color has no good default, so the engine must ask for it.
