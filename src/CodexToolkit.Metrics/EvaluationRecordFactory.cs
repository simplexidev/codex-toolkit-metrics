namespace CodexToolkit.Metrics;

public static class EvaluationRecordFactory
{
    public static EvaluationRecord Create(
        EvaluationPlan plan,
        EvaluationScenario scenario,
        EvaluationArmDefinition arm,
        RawTrialResult trial,
        IReadOnlyList<RawTrialResult> repetitions,
        string planDirectory)
    {
        var assertionsPassed = trial.Assertions.Count(assertion => assertion.Passed);
        var deterministicOutcome = assertionsPassed == trial.Assertions.Count && trial.Execution.Failure is null
            ? GateOutcome.Pass : GateOutcome.Fail;
        var finalOutcome = deterministicOutcome == GateOutcome.Fail
            ? GateOutcome.Fail
            : trial.Judgment.Outcome == GateOutcome.NotApplicable
                ? GateOutcome.Pass
                : trial.Judgment.Outcome;

        return new EvaluationRecord
        {
            SchemaVersion = EvaluationRecord.CurrentSchemaVersion,
            Identity = new EvaluationIdentity
            {
                Scenario = scenario.Id,
                Capability = scenario.Capability,
                Skill = Names(arm.SkillPaths),
                Agent = Names(arm.AgentPaths),
                Arm = arm.Id,
                Executor = new ExecutorIdentity { Model = plan.Executor.Model, Reasoning = plan.Executor.Reasoning },
                Judge = new JudgeIdentity
                {
                    Method = plan.Judge.Path switch
                    {
                        JudgingPath.Deterministic => JudgeMethod.Deterministic,
                        JudgingPath.Jev => JudgeMethod.Deterministic,
                        _ => JudgeMethod.Gpt
                    },
                    Model = plan.Judge.Path == JudgingPath.Gpt ? plan.Judge.Model : null
                },
                Toolkit = plan.Provenance.Toolkit,
                Metrics = plan.Provenance.Metrics,
                Upstream = plan.Provenance.Upstream,
                Timestamp = trial.Timestamp,
                Repetition = trial.Repetition
            },
            Arm = new EvaluationArm { Comparison = arm.Comparison, Customization = arm.Customization },
            Quality = new QualityMetrics
            {
                Completion = trial.Execution.Failure is null ? GateOutcome.Pass : GateOutcome.Fail,
                DeterministicAssertions = Assertions(trial.Assertions.Count, assertionsPassed, deterministicOutcome),
                SafetyAssertions = Assertions(0, 0, GateOutcome.NotApplicable),
                SemanticRubric = trial.Judgment.Score is { } score
                    ? Metric(score, "ratio", MeasurementKind.Measured, trial.Judgment.Method)
                    : Unavailable("ratio", trial.Judgment.Method),
                Activation = Routing(scenario.Expected.ActivatedSkills, trial.Execution.Observations.ActivatedSkills),
                Delegation = Routing(scenario.Expected.DelegatedAgents, trial.Execution.Observations.DelegatedAgents),
                NestedDelegation = new NestedDelegationMetrics
                {
                    Outcome = GateOutcome.NotApplicable,
                    Correctness = Unavailable("ratio", "nested delegation not requested")
                },
                FinalQualityGate = finalOutcome
            },
            Efficiency = new EfficiencyMetrics
            {
                GptInputTokens = Optional(trial.Execution.Usage.InputTokens, "tokens", "Codex JSONL usage"),
                GptOutputTokens = Optional(trial.Execution.Usage.OutputTokens, "tokens", "Codex JSONL usage"),
                GptTotalTokens = Optional(trial.Execution.Usage.TotalTokens, "tokens", "Codex JSONL usage"),
                Turns = Optional(trial.Execution.Usage.Turns, "count", "Codex JSONL events"),
                ToolCalls = Optional(trial.Execution.Usage.ToolCalls, "count", "Codex JSONL events"),
                FilesInContext = Unavailable("count", "executor did not expose files in context"),
                ContextTokens = Unavailable("tokens", "executor did not expose context tokens"),
                Subagents = Metric(trial.Execution.Observations.DelegatedAgents.Count, "count", MeasurementKind.Measured, "Codex JSONL observations"),
                FullBuilds = Unavailable("count", "build classification unavailable"),
                TargetedBuilds = Unavailable("count", "build classification unavailable"),
                FullTests = Unavailable("count", "test classification unavailable"),
                TargetedTests = Unavailable("count", "test classification unavailable"),
                ElapsedTime = Metric((decimal)trial.Execution.Elapsed.TotalSeconds, "seconds", MeasurementKind.Measured, "monotonic process timer")
            },
            Jev = EmptyJev(),
            StaticCost = StaticCost(arm, planDirectory),
            Statistics = new StatisticsMetrics
            {
                BaselineCompatibility = new BaselineCompatibility
                {
                    Hash = trial.CompatibilityHash,
                    Version = trial.CompatibilityVersion
                },
                Summaries = Summaries(repetitions)
            }
        };
    }

    private static AssertionMetrics Assertions(int total, int passed, GateOutcome outcome) => new()
    {
        Total = Metric(total, "count", MeasurementKind.Measured, "deterministic assertion runner"),
        Passed = Metric(passed, "count", MeasurementKind.Measured, "deterministic assertion runner"),
        Outcome = outcome
    };

    private static RoutingQualityMetrics Routing(IReadOnlyList<string> expected, IReadOnlyList<string> observed)
    {
        if (expected.Count == 0)
        {
            return new RoutingQualityMetrics
            {
                Correctness = Unavailable("ratio", "no routing expectation"),
                False = Metric(observed.Count, "count", MeasurementKind.Measured, "structured executor observations"),
                Missed = Metric(0, "count", MeasurementKind.Measured, "structured executor observations")
            };
        }

        var matches = expected.Intersect(observed, StringComparer.Ordinal).Count();
        return new RoutingQualityMetrics
        {
            Correctness = Metric((decimal)matches / expected.Count, "ratio", MeasurementKind.Derived, "expected/observed set intersection"),
            False = Metric(observed.Except(expected, StringComparer.Ordinal).Count(), "count", MeasurementKind.Derived, "expected/observed set difference"),
            Missed = Metric(expected.Except(observed, StringComparer.Ordinal).Count(), "count", MeasurementKind.Derived, "expected/observed set difference")
        };
    }

    private static JevIntelligenceMetrics EmptyJev() => new()
    {
        DeterministicResolutions = Unavailable("count", "full JEV judging deferred"),
        PurposeResolutions = Unavailable("count", "full JEV judging deferred"),
        InputResolutions = Unavailable("count", "full JEV judging deferred"),
        CostResolutions = Unavailable("count", "full JEV judging deferred"),
        ConfidenceResolutions = Unavailable("count", "full JEV judging deferred"),
        OutcomeResolutions = Unavailable("count", "full JEV judging deferred"),
        Fallbacks = Unavailable("count", "full JEV judging deferred"),
        Escalations = Unavailable("count", "full JEV judging deferred"),
        FalseExclusions = Unavailable("count", "full JEV judging deferred"),
        ContextAvoided = Unavailable("tokens", "full JEV judging deferred")
    };

    private static StaticCostMetrics StaticCost(EvaluationArmDefinition arm, string planDirectory) => new()
    {
        Skill = new SkillStaticCost
        {
            Trigger = Unavailable("tokens", "trigger metadata measurement deferred"),
            SkillFile = FileBytes(arm.SkillPaths, planDirectory, "configured skill files"),
            LazyReferences = Unavailable("tokens", "lazy-reference classification deferred"),
            AlwaysVisible = Unavailable("tokens", "visibility classification deferred"),
            ActivationVisible = Unavailable("tokens", "visibility classification deferred")
        },
        Agent = new AgentStaticCost
        {
            Configuration = FileBytes(arm.AgentPaths, planDirectory, "configured agent files"),
            Instructions = Unavailable("tokens", "instruction classification deferred")
        }
    };

    private static NumericMetric FileBytes(IReadOnlyList<string> paths, string planDirectory, string method)
    {
        long total = 0;
        foreach (var configuredPath in paths)
        {
            var path = CompatibilityHasher.Resolve(planDirectory, configuredPath);
            total += File.Exists(path)
                ? new FileInfo(path).Length
                : CompatibilityHasher.EnumerateRegularFiles(path).Sum(file => new FileInfo(file).Length);
        }

        return Metric(total, "bytes", MeasurementKind.Measured, method);
    }

    private static IReadOnlyList<StatisticalSummary> Summaries(IReadOnlyList<RawTrialResult> trials) =>
    [
        Summary("efficiency.elapsedTime", trials.Select(item => (decimal)item.Execution.Elapsed.TotalSeconds).ToArray(), "seconds"),
        Summary("quality.assertionPassRate", trials.Select(item => item.Assertions.Count == 0 ? 1m :
            (decimal)item.Assertions.Count(result => result.Passed) / item.Assertions.Count).ToArray(), "ratio")
    ];

    private static StatisticalSummary Summary(string name, IReadOnlyList<decimal> values, string unit)
    {
        var ordered = values.Order().ToArray();
        var mean = values.Average();
        var median = ordered.Length % 2 == 1 ? ordered[ordered.Length / 2] :
            (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2;
        return new StatisticalSummary
        {
            Metric = name,
            Repetitions = Metric(values.Count, "count", MeasurementKind.Derived, "trial count"),
            Mean = Metric(mean, unit, MeasurementKind.Derived, "arithmetic mean"),
            Median = Metric(median, unit, MeasurementKind.Derived, "ordered sample median"),
            Minimum = Metric(ordered[0], unit, MeasurementKind.Derived, "sample minimum"),
            Maximum = Metric(ordered[^1], unit, MeasurementKind.Derived, "sample maximum"),
            ConfidenceInterval = new ConfidenceInterval
            {
                Level = Metric(0.95m, "ratio", MeasurementKind.Estimated, "bounded empirical interval"),
                Lower = Metric(ordered[0], unit, MeasurementKind.Estimated, "bounded empirical interval"),
                Upper = Metric(ordered[^1], unit, MeasurementKind.Estimated, "bounded empirical interval"),
                Method = "bounded empirical interval",
                Justification = values.Count < 3
                    ? "Too few repetitions for a distributional interval; report the observed range."
                    : "Conservative observed range avoids unsupported distribution assumptions."
            }
        };
    }

    private static NumericMetric Optional(long? value, string unit, string method) => value is { } number
        ? Metric(number, unit, MeasurementKind.Measured, method)
        : Unavailable(unit, method + "; unavailable in executor output");

    private static NumericMetric Metric(decimal value, string unit, MeasurementKind kind, string method) =>
        new() { Value = value, Unit = unit, Kind = kind, Method = method };

    private static NumericMetric Unavailable(string unit, string method) =>
        new() { Value = null, Unit = unit, Kind = MeasurementKind.Unavailable, Method = method };

    private static string? Names(IReadOnlyList<string> paths) => paths.Count == 0
        ? null
        : string.Join(",", paths.Select(Path.GetFileName));
}
