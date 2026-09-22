# Dashboard source

This directory contains a dependency-free static dashboard that reads only sanitized data
from `data/public/`. Serve the repository root locally to preview it; opening the HTML file
directly may prevent JSON loading because of browser origin rules.

There is deliberately no Pages deployment workflow in this bootstrap. A future publishing
phase must validate and stage only approved public data for the target
<https://simplexidev.github.io/codex-toolkit-metrics/>.
