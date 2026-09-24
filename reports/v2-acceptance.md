# Codex Toolkit v2 acceptance report

Decision: **REVISE**

Subject: `simplexidev/codex-toolkit` revision
`5a4b35e5ccd96392e91a11c4048c4497cdcee655` on `develop/v2.0.0`.
Measurement date: 2026-09-24. Public aggregates are sanitized; prompts, responses,
transcripts, raw logs, and local paths remain outside version control.

## Quality and safety gates

The optimized arm passed 17 of 17 bounded scenarios (100%), compared with 88.24% across
the combined reviewed pre-v2 baseline and 92.86% for both the VANILLA and UPSTREAM skill
arms. No net quality regression was detected. The current toolkit also passed 175 compiled
tests, structural validation of 69 JSON documents, and all 29 offline skill fixture-integrity
checks. The agent candidate plan and reviewed recommendations still align with the current
toolkit metadata. Execution and judging remained OpenAI/GPT-only; this acceptance run used
deterministic judging because its assertions resolved every case.

These gates establish bounded regression safety, not complete behavioral coverage. The
scenario prompts carry observable completion markers, semantic judging was not exercised,
and the offline toolkit eval explicitly measures fixture integrity rather than agent behavior.
Only 17 bounded scenarios exist for 31 declared capability groups (54.84% conservative
evidence coverage), so overall acceptance remains REVISE despite the perfect bounded pass
rate.

## Efficiency after quality

One bounded repetition per arm supports directional comparisons only.

| Comparison | Pass rate | Mean tokens | Mean time |
| --- | ---: | ---: | ---: |
| VANILLA skills | 92.86% | 38,991 | 16.49 s |
| UPSTREAM skills | 92.86% | 53,222 | 18.72 s |
| OPTIMIZED skills | 100% | 45,626 | 21.09 s |
| BUILTIN agents | 100% | 81,911 | 22.91 s |
| PREVIOUS custom agent | 100% | 67,368 | 18.69 s |
| OPTIMIZED agent boundary | 100% | 33,584 | 16.34 s |

The optimized skill arm used about 14.3% fewer tokens than UPSTREAM but 17.0% more than
VANILLA. It was about 12.7% slower than UPSTREAM and 27.9% slower than VANILLA. The
optimized agent-boundary arm used about half the tokens of the previous custom-agent arm
and was about 12.5% faster, but delegation itself was not observed, so this is a boundary
prompt comparison rather than proof of custom-agent execution efficiency.

The v2 static inventory contains 29 skills and one custom agent. Estimated always-visible
context rose from 535 to 711 tokens and maximum possible skill load from 3,329 to 5,806
tokens as coverage expanded. Mean trigger overlap improved from 0.0335 to 0.0286 and the
maximum remained 0.4. This is acceptable for the candidate because the added content is
primarily activation-visible, but it should remain a release watch item.

## Skill decisions

| Surface | Decision | Evidence |
| --- | --- | --- |
| `run-dotnet-tests`, `write-dotnet-tests`, `dotnet-test-quality`, `dotnet-coverage` | ACCEPT | Optimized testing scenarios cleared the quality gate and the bundle beat UPSTREAM token cost. |
| `diagnose-build`, `optimize-build`, `diagnose-dotnet`, `investigate-dotnet-performance` | ACCEPT | Compiler, MSBuild, performance, and diagnostic-planning cases passed; deterministic toolkit tests also passed. |
| `ci-triage`, `repo-health` | ACCEPT | CI and general-repository cases passed and deterministic commands validated. |
| Remaining 19 toolkit skills | ACCEPT | No bounded regression was observed; all compiled and fixture-integrity gates passed. This does not claim independent live behavioral evidence for every skill. |
| Optional advanced .NET capabilities sourced from `dotnet/skills` | KEEP_UPSTREAM | The toolkit intentionally references rather than vendors advanced upstream workflows. |

No skill met the evidence threshold for REVERT_TO_UPSTREAM or RETIRE.

## Agent decisions

| Agent | Decision | Evidence |
| --- | --- | --- |
| `reviewer` | ACCEPT | Retained read-only boundary, prior quality parity, current metadata alignment, and passing optimized review case. |
| historical `code-mapper` | REPLACE_WITH_BUILTIN | Prior bounded evidence favored deterministic narrowing plus a generic built-in subagent. |
| historical `log-analyzer` | RETIRE | Structured summarizers and focused diagnostic skills cover the broad role with less routing ambiguity. |
| `security-reviewer` | INSUFFICIENT_EVIDENCE | One prior fixture showed no quality gain over reviewer or generic baselines. |
| `runtime-diagnostician` | INSUFFICIENT_EVIDENCE | Prior quality tied the generic baseline; the time signal was underpowered. |
| `build-diagnostician` | INSUFFICIENT_EVIDENCE | No quality gain and worse cost than normal bounded reasoning. |
| `test-specialist` | INSUFFICIENT_EVIDENCE | Focused testing skills plus normal reasoning matched the candidate. |
| `upstream-reviewer` | INSUFFICIENT_EVIDENCE | The candidate underperformed the built-in comparator. |
| `release-auditor` | INSUFFICIENT_EVIDENCE | No measured quality or cost advantage over deterministic release gates. |

## Capability and instrumentation decisions

| Area | Decision | Required follow-up |
| --- | --- | --- |
| Bounded v2 quality/safety regression suite | ACCEPT | Preserve the 17-case aggregate as the candidate snapshot. |
| Deterministic AgentTool contribution | ACCEPT | Preserve the 175-test and structural-validation gates. |
| Full declared capability evidence | REVISE | Add affected, behavior-measuring cases for uncovered capability groups rather than broadening every run. |
| Routing, delegation, tools, files/context, and build/test classification | REVISE | Capture structured observations; the current run exposed zero tool/delegation observations and no files/context or build/test classification. |
| JEV calls, cost, fallback, and escalation instrumentation | ACCEPT | The keyless deterministic run correctly recorded zero remote calls, fallbacks, and escalations. |
| JEV false exclusions and context avoided | REVISE | Both have 0% measurement coverage. Add calibrated ground truth and a bounded avoided-context comparator before claiming benefit. |

The final toolkit review should accept the implemented skill and agent dispositions, while
treating capability/instrumentation expansion as targeted follow-up. Efficiency signals
must not overturn the passing quality gates, and the missing evidence must not be reported
as zero cost or zero risk.
