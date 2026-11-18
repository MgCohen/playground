The spec is a software design request. The set is a list of patterns. Each line is one pattern: what it is, when to use it, its variant, and its typed ports. Code joins the patterns of the set by their port types.

- A job rubric is true if one pattern of the set, or the patterns together, do that job, by their "use when" text, their variant and their ports.
- A pattern does only what its line says. Do not add a step, a delay, a retry or a check that the line does not name.
- A "do not use when" text that matches the request means that the pattern does not do the job.
- Use only the patterns in the set. Do not use what a pattern name suggests: use only the listed text.
