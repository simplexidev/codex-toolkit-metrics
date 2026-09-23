# Scenarios

Versioned, non-sensitive evaluation scenario definitions live here. Scenario metadata may
describe capability, expected structured outcome, cost class, routing behavior, and
delegation behavior. Private source text and raw prompts do not belong here.

The bootstrap fixture is intentionally synthetic. Future schema changes should be
versioned and paired with evaluator tests and calibration evidence.

Runner plans select a fixture root, repetitions, timeout, OpenAI executor model, judging
boundary, provenance, arms, and deterministic expectations. Skill directories are copied
to `.codex/skills/` and agent files/directories to `.codex/agents/` inside each isolated
trial workspace. Plan-relative prompt files are supported, but private prompts must stay
outside this repository.

`routing-delegation-v1.json` covers positive, negative and ambiguous skill routing plus
correct/false/missed agent delegation, nested delegation, invoked tools and context
isolation. Correct versus missed outcomes are derived from expected/observed sets;
negative cases count any observed activation or delegation as false. Replace provenance
placeholders and executor version before recording a real run.

The `pre-optimization-dotnet-v1.json` and `pre-optimization-agents-v1.json` plans capture
the reusable v2 baseline before major toolkit optimization. They use one repetition per
arm: VANILLA versus the pinned current `dotnet/skills` snapshot, and built-in/no-custom
versus the current custom reviewer. The upstream checkout remains local at the relative
path declared by the plan; it is not vendored. Run with `--reuse-baseline` so exact
compatibility matches are reused and changed inputs are rerun. The public synthetic
fixture contains no private source; raw executor events and responses remain private.
