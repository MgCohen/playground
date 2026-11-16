# Authoring guide: topology

- A new pattern lists its typed ports: `inputs` and `outputs`, each `{ name, type }`. The types are Request, Command, Event, Saved, Query, View and Effect.
- Use only existing components and blocks as parts.
- Name the job of the pattern in `use_when`, so that it does not also match an existing pattern.
