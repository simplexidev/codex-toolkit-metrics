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
- `dashboard/` — static dashboard source. Pages deployment is intentionally not enabled.

Raw prompts, responses, transcripts, private source, and logs must remain local or in
short-lived CI artifacts. Only reviewed aggregates accepted by
[`schemas/public-metrics-v1.schema.json`](schemas/public-metrics-v1.schema.json) may be
committed or eventually published.

## Develop

The repository requires the .NET SDK selected by `global.json`.

```console
dotnet restore CodexToolkit.Metrics.slnx
dotnet test CodexToolkit.Metrics.slnx
dotnet run --project src/CodexToolkit.Metrics -- validate-public data/public/example-summary.json
dotnet run --project src/CodexToolkit.Metrics -- dashboard-check dashboard
```

The future GitHub Pages target is
<https://simplexidev.github.io/codex-toolkit-metrics/>. This bootstrap does not publish or
configure Pages.
