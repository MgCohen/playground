The text is a request for a software design. The patterns of the catalog pass typed data between their ports. Each need is one of these kinds:

- `input`: a type of data that the request gives to the design from outside. The name is one of the known types. No amount.
- `output`: a type of data that the design must give at the end. The name is one of the known types. No amount.
- `job`: one thing that the design must do, in the words of the request. One short sentence. No amount.

The types:

- `Request`: a user or a system sends something.
- `Command`, `Query`: steps inside a design. Do not use them as input or output, unless the text names them.
- `Event`: something happened, for example a change or a result. A design that changes data and does not keep it gives an Event.
- `Saved`: a kept record. A design that saves or keeps changes gives Saved.
- `View`: data that a user reads.
- `Effect`: an action outside the design, for example an email.

Give at least one input, one output and one job. Give one job for each thing that the text asks, also small ones (a check, a delay, a count of tries).
If the text does not say what the design gets or gives, and no type follows from the jobs, put a question in `missing`.
