# Topology: subject rules

You design small software topologies as JSON. Each block is one pattern instance: a design pattern made of components.

## A block

```json
{ "p": "query_handler", "v": "plain",
  "parts": [ { "id": "in", "p": "query_endpoint" }, { "id": "read", "p": "reader" } ],
  "wires": [ ["request", "in.request"], ["in.query", "read.query"], ["read.view", "view"] ] }
```

- `p` is the pattern id. `v` is the variant id. A component (a leaf part) has only `id` and `p`.
- `parts` holds the child blocks and components. Each part has a unique `id` inside its parent.
- `wires` is a list of [from, to] pairs. `from` is an input of the block or an output of a part (`in.query`). `to` is an input of a part or an output of the block.
- Each port has a type: Request, Command, Event, Saved, Query, View or Effect. A wire joins two ports of the same type.
- Drive each input of each part and each output of the block with exactly one wire. Do not make a loop.
- The ports of each pattern are in its `inputs` and `outputs`. A new pattern also lists its `inputs` and `outputs` in the block.

## Components

| Component | Input | Output |
|---|---|---|
| endpoint | request: Request | command: Command |
| validator | command: Command | command: Command |
| handler | command: Command | event: Event |
| event_store | event: Event | saved: Saved |
| query_endpoint | request: Request | query: Query |
| reader | query: Query | view: View |
| subscriber | event: Event | effect: Effect |
