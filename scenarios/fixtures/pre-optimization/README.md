# Synthetic pre-optimization fixture

This small, public .NET workspace supports repeatable planning and diagnosis scenarios.
It contains intentionally conflicting evidence: a passing unit test target, a missing edge
case, compiler and MSBuild failure excerpts, a slow-build summary, coverage data, an
application log, a focused patch, and CI metadata. Evaluation prompts are read-only, so
every arm sees the same fixture and no raw repository content is published.

