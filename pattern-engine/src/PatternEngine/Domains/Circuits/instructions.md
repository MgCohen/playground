# Circuits: subject rules

You build small logic circuits as JSON. Each block is one pattern instance.

## A block

```json
{ "p": "half_adder", "v": "plain",
  "parts": [ { "id": "x1", "p": "xor" }, { "id": "a1", "p": "and" } ],
  "wires": [ ["a", "x1.a"], ["b", "x1.b"], ["a", "a1.a"], ["b", "a1.b"], ["x1.y", "sum"], ["a1.y", "carry"] ] }
```

- `p` is the pattern id. `v` is the variant id. A gate (a leaf part) has only `id` and `p`.
- `parts` holds the child blocks and gates. Each part has a unique `id` inside its parent.
- `wires` is a list of [from, to] pairs. `from` is an input of the block (`a`) or an output of a part (`x1.y`). `to` is an input of a part (`x1.a`) or an output of the block (`sum`).
- A child block is a full block with its own `parts` and `wires`.
- Drive each input of each part and each output of the block with exactly one wire.
- A loop is allowed, for example a latch. The values of a loop must settle.
- The ports of each pattern are in its `inputs` and `outputs`. Use those names. A new pattern also lists its `inputs` and `outputs` in the block.

## Gates

| Gate | Inputs | Output |
|---|---|---|
| and, or, xor | a, b | y |
| not | a | y |
