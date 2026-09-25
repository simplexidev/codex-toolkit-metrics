# Metrics repository development

This repository owns evaluation, measurement, calibration, schemas, scenarios, and
publication tooling for `simplexidev/sdeveng`. Reviewed aggregate history belongs in
`simplexidev/sdeveng-metrics-data`; static presentation and Pages deployment belong in
`simplexidev/sdeveng-metrics-dashboard`. Do not add either owned tree here.

Keep evaluator and validation logic deterministic where possible. Use bounded semantic
judgment only where deterministic measurement cannot answer the question. Evaluation
executors and judges must use OpenAI/GPT models only; do not add Claude-based execution
or judging. Normal tests and CI must be keyless and use synthetic or mocked inputs.

Raw runs, prompts, responses, transcripts, private source, and other sensitive evaluation
artifacts stay in ignored local storage or short-lived CI artifacts. Only reviewed,
sanitized aggregates that conform to the versioned public schema may be committed to the
data repository or included in dashboard output. Never persist, print, serialize, or pass
`TYPESAFE_API_KEY` to unrelated processes.

Keep deep human guidance in `simplexidev/sdeveng-docs`. Short repository-operational
notes are appropriate here. Pages publishes from the dashboard repository at
<https://simplexidev.github.io/sdeveng-metrics-dashboard/>.

Use .NET 10 for executable evaluator code. Add automated tests for measurement, schema,
sanitization, judging, or dashboard behavior changes. Preserve unrelated work and keep
generated build/test output out of version control.
