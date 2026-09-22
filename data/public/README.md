# Public metrics data

Only sanitized, reviewed aggregate data belongs here. Every JSON metrics document must
conform to `schemas/public-metrics-v1.schema.json` and pass the repository validator.

Do not include prompts, responses, transcripts, private source, local paths, user data,
secrets, or raw logs. Preserve historical aggregate files rather than silently rewriting
published history; corrections should be explicit in a later phase's provenance policy.
