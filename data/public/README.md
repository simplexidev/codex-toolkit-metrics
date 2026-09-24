# Public metrics data

Only sanitized, reviewed aggregate data belongs here. Every JSON metrics document must
conform to `schemas/public-metrics-v1.schema.json` and pass the repository validator.
Every JSON file must also be explicitly listed in `publication-manifest.json`; otherwise
publication fails closed.
Estimated values should include `kind` and `method`; `static-cost-routing.json` is
generated from the sibling toolkit with `measure-static --public-output` and contains no
source text.

`pre-optimization-baseline.json` is generated from reviewed private evaluation records
with `aggregate-baseline`. It publishes arm-level pass rate, token, elapsed-time,
tool-call, and delegation aggregates only. Preserve its compatible raw inputs locally so
later optimization phases can reuse or compare them without publishing trial content.

Do not include prompts, responses, transcripts, private source, local paths, user data,
secrets, or raw logs. Preserve historical aggregate files rather than silently rewriting
published history; corrections should be explicit in a later phase's provenance policy.
Judge calibration may publish only the aggregate produced by `calibrate-judges
--public-output`; its example-level report always remains in private storage.
