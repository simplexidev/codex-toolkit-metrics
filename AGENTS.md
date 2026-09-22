# Metrics repository development

This repository owns evaluation, measurement, calibration, sanitized metrics history,
and dashboard implementation for `simplexidev/codex-toolkit`. It may inspect or execute
that repository as the subject under test, but it must consume stable structured outputs
and must never become a Codex plugin or runtime dependency.

Keep evaluator and validation logic deterministic where possible. Use bounded semantic
judgment only where deterministic measurement cannot answer the question. Evaluation
executors and judges must use OpenAI/GPT models only; do not add Claude-based execution
or judging. Normal tests and CI must be keyless and use synthetic or mocked inputs.

Raw runs, prompts, responses, transcripts, private source, and other sensitive evaluation
artifacts stay in ignored local storage or short-lived CI artifacts. Only reviewed,
sanitized aggregates that conform to the versioned public schema may be committed under
`data/public/` or included in dashboard output. Never persist, print, serialize, or pass
`TYPESAFE_API_KEY` to unrelated processes.

Keep deep human guidance in `simplexidev/codex-toolkit-docs`. Short repository-operational
notes are appropriate here. Pages will eventually publish at
<https://simplexidev.github.io/codex-toolkit-metrics/>, but do not add or enable a Pages
deployment without an explicit phase requesting it.

Use .NET 10 for executable evaluator code. Add automated tests for measurement, schema,
sanitization, judging, or dashboard behavior changes. Preserve unrelated work and keep
generated build/test output out of version control.
