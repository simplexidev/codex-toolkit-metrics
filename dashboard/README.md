# Dashboard source

This directory contains a dependency-free static dashboard. The publication command copies
only manifest-approved aggregates into the generated site's `data/` directory. Serve the
generated `_site/` directory locally to preview it; opening the HTML file directly may
prevent JSON loading because of browser origin rules.

```console
dotnet run --project src/SdevEng.Metrics -- publish-pages dashboard data/public _site
```

All browser paths are project-relative for the Pages target at
<https://simplexidev.github.io/codex-toolkit-metrics/>.
