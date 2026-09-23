# Codex Toolkit Metrics

Evaluation, measurement, calibration, sanitized metrics history, and dashboard source for
[`simplexidev/codex-toolkit`](https://github.com/simplexidev/codex-toolkit).

This repository treats Codex Toolkit as the subject under test. It may run the toolkit and
consume its stable structured outputs, but it is not a plugin and is not a runtime
dependency. Human-facing methodology and usage documentation belongs in
[`simplexidev/codex-toolkit-docs`](https://github.com/simplexidev/codex-toolkit-docs).

## Repository layout

- `src/` — .NET 10 evaluator, statistics, sanitization, and validation code.
- `tests/` — keyless tests and synthetic fixtures.
- `scenarios/` — versioned scenario definitions without private prompts or source.
- `schemas/` — stable schemas for committed and published data.
- `data/public/` — reviewed, sanitized aggregate history suitable for publication.
- `data/private/` — ignored local raw runs; never commit this directory's contents.
- `dashboard/` — dependency-free static dashboard source, published through GitHub Pages.

Raw prompts, responses, transcripts, private source, and logs must remain local or in
short-lived CI artifacts. Only reviewed aggregates accepted by
[`schemas/public-metrics-v1.schema.json`](schemas/public-metrics-v1.schema.json) may be
committed or published. `data/public/publication-manifest.json` is the explicit allowlist;
the publisher rejects unlisted JSON, raw fields, logs, secret-like content, local absolute
paths, and malformed aggregates before creating the Pages artifact.

Run-level evaluator output uses the typed C# data model and
[`schemas/evaluation-record-v1.schema.json`](schemas/evaluation-record-v1.schema.json).
Every numeric metric carries a `kind` (`measured`, `derived`, `estimated`, or
`unavailable`), unit, and method. The [schema lifecycle policy](schemas/README.md)
defines compatibility and migration rules.

## Evaluation runner

The runner executes only through the OpenAI-backed Codex CLI. Executor and GPT-judge
model IDs are plan settings; non-OpenAI providers and Claude model identifiers are
rejected. Plans also require an executor version tag so CLI upgrades invalidate cached
baselines. Each arm/repetition receives an isolated copy of its fixture plus configured
skill and agent overlays. Timeouts are bounded and terminate the complete process tree.

Plans conform to
[`schemas/evaluation-plan-v1.schema.json`](schemas/evaluation-plan-v1.schema.json). The
[`scenarios/runner-smoke-v1.json`](scenarios/runner-smoke-v1.json) plan is a synthetic
example; replace its provenance placeholders before recording a real run.

```console
dotnet run --project src/CodexToolkit.Metrics -- validate-plan scenarios/runner-smoke-v1.json
dotnet run --project src/CodexToolkit.Metrics -- run scenarios/runner-smoke-v1.json
dotnet run --project src/CodexToolkit.Metrics -- run scenarios/runner-smoke-v1.json --reuse-baseline
dotnet run --project src/CodexToolkit.Metrics -- run scenarios/runner-smoke-v1.json --raw-dir /private/evaluation/path
```

Raw prompts, JSONL events, responses, failures, and per-trial records are written beneath
the selected private raw directory (default `data/private/runs/`, which is ignored).
Console output contains only compact pass/fail and reuse counts. Compatibility hashing
covers scenario expectations and prompts, fixture and overlay content, executor/model/
reasoning/executable settings, timeout, judge settings, and runner compatibility version.
An exact hash/version match is required for reuse; existing mismatches are stale and run
again.

Judging has three explicit boundaries: deterministic assertions are implemented, the JEV
path records a deferred/not-applicable semantic result, and the GPT path invokes a
bounded OpenAI judge that must return a score in `[0,1]`. Full JEV judging is intentionally
deferred.

## Static cost and routing measurement

The deterministic analyzer inventories the sibling toolkit's skill frontmatter, complete
`SKILL.md` files, lazy Markdown references, maximum possible load, always-visible routing
surface, activation-visible content, sibling trigger overlap, and custom-agent
configuration/instructions. Its detailed local report records exact UTF-8 bytes and
characters. Token counts use a GPT-family `ceil(UTF-8 bytes / 4)` approximation because no
model tokenizer is available in the keyless evaluator; every such value is labeled
`estimated` with the method.

```console
dotnet run --project src/CodexToolkit.Metrics -- measure-static ../codex-toolkit
dotnet run --project src/CodexToolkit.Metrics -- measure-static ../codex-toolkit \
  --output /private/path/static-report.json \
  --public-output data/public/static-cost-routing.json
```

Treat the detailed report as local evaluator output. `--public-output` emits only
schema-valid sanitized aggregates for the dashboard. The versioned
`routing-delegation-v1.json` plan distinguishes should-activate, should-not-activate and
ambiguous routing. Expected/observed sets calculate false and missed activation or
delegation; structured observations also measure invoked tools, maximum delegation depth
and context isolation. Ambiguous cases are observed but left unscored.

## Develop

The repository requires the .NET SDK selected by `global.json`.

```console
dotnet restore CodexToolkit.Metrics.slnx
dotnet test CodexToolkit.Metrics.slnx
dotnet run --project src/CodexToolkit.Metrics -- validate-evaluation tests/CodexToolkit.Metrics.Tests/Fixtures/evaluation-valid-v1.json
dotnet run --project src/CodexToolkit.Metrics -- validate-public data/public/example-summary.json
dotnet run --project src/CodexToolkit.Metrics -- dashboard-check dashboard
dotnet run --project src/CodexToolkit.Metrics -- publish-pages dashboard data/public _site
dotnet run --project src/CodexToolkit.Metrics -- validate-plan scenarios/routing-delegation-v1.json
```

The GitHub Actions Pages deployment publishes the generated, sanitized artifact at
<https://simplexidev.github.io/codex-toolkit-metrics/>. Dashboard code uses only relative
paths so the site works beneath the `/codex-toolkit-metrics/` project base.
