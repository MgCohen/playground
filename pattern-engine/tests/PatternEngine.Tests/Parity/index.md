# Pattern index: shapes

Read the full file `patterns/<id>.md` before you use a pattern.

## Composed patterns

| id | level | status | use when | do not use when | variants |
|---|---|---|---|---|---|
| `street` | 1 | stable | The request names a street, a road or a row of two or more buildings or trees. | The request names only one building or one tree. → use `house` | `row`: Always. The objects stand in one row on the ground. |
| `house` | 2 | stable | The request names a house or a home. | The building is tall, narrow, or taller than it is wide. → use `tower`<br>The building is for tools, storage, bikes or a car. → use `shed` | `plain`: No chimney is named.<br>`chimney`: The request names a chimney or smoke. |
| `shed` | 2 | stable | The request names a shed, a garage or a tool hut. | People live in the building. → use `house` | `plain`: Always. |
| `tower` | 2 | stable | The request names a tower, or a tall or narrow building. | The building is low and wider than it is tall. → use `house`<br>The tower holds a light, a lamp or sails, for example a lighthouse or a windmill. → no pattern fits | `plain`: Always. |
| `tree` | 2 | stable | The request names a tree. | — | `plain`: Always. |

## Leaf parts

| id | level | primitive |
|---|---|---|
| `body` | 3 | rect |
| `chimney` | 3 | rect |
| `crown` | 3 | circle |
| `door` | 3 | rect |
| `ground` | 3 | rect |
| `roof` | 3 | triangle |
| `trunk` | 3 | rect |
| `window` | 3 | rect |
