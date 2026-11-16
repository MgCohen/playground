---
id: party-eval
kind: review
level: pack
score: { rule: all }
---
The eval of a party run. Code checks each rubric with the oracle. The run never gets this anchor.

- id: valid
  ask: Is each quest rubric true for the party, or is "none" correct?
  check: code
  args: { rungs: [cover, budget, available, many, stats, prose] }
  visibility: eval
- id: smallest
  ask: Is the party size equal to the smallest size?
  check: code
  args: { rungs: [cover] }
  visibility: eval
- id: best-cost
  ask: Is the cost equal to the best cost?
  check: code
  args: { rungs: [budget, available, stats, prose] }
  visibility: eval
- id: best-total
  ask: Is the total cost equal to the best total?
  check: code
  args: { rungs: [many] }
  visibility: eval
- id: spec
  ask: Are the decomposed rubrics equal to the hidden rubrics?
  check: code
  args: { rungs: [prose] }
  visibility: eval
