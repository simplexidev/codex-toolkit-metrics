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
                Agent = arm.Role ?? Names(arm.AgentPaths),
                Arm = arm.Id,
                Executor = new ExecutorIdentity { Provider = plan.Executor.Provider, Model = plan.Executor.Model, Reasoning = plan.Executor.Reasoning },
                Judge = new JudgeIdentity
                {
                    Method = plan.Judge.Path switch
                    {
                        JudgingPath.Deterministic => JudgeMethod.Deterministic,
                        JudgingPath.Jev => JudgeMethod.Hybrid,
                        _ => JudgeMethod.Gpt
                    },
                    Model = plan.Judge.Path == JudgingPath.Deterministic ? null : plan.Judge.Model
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
                Activation = Routing(scenario.Expected.ActivatedSkills, trial.Execution.Observations.ActivatedSkills, scenario.Expected.ActivationCase),
                Delegation = Routing(scenario.Expected.DelegatedAgents, trial.Execution.Observations.DelegatedAgents, scenario.Expected.DelegationCase),
                InvokedTools = Routing(scenario.Expected.InvokedTools, trial.Execution.Observations.InvokedTools,
                    scenario.Expected.InvokedTools.Count == 0 ? RoutingCase.Unspecified : RoutingCase.ShouldActivate),
                NestedDelegation = NestedDelegation(scenario.Expected.NestedDelegation, trial.Execution.Observations.MaximumDelegationDepth),
                ContextIsolation = Compare(scenario.Expected.ContextIsolation, trial.Execution.Observations.ContextIsolated),
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
                ElapsedTime = Metric((decimal)trial.Execution.Elapsed.TotalSeconds, "seconds", MeasurementKind.Measured, "monotonic process timer"),
                JevJudgeCalls = Metric(trial.Judgment.JevUsage.RemoteCalls, "calls", MeasurementKind.Measured, "JEV judge instrumentation"),
                GptJudgeInputTokens = Optional(trial.Judgment.Usage.InputTokens, "tokens", "GPT judge Codex JSONL usage"),
                GptJudgeOutputTokens = Optional(trial.Judgment.Usage.OutputTokens, "tokens", "GPT judge Codex JSONL usage"),
                GptJudgeTotalTokens = Optional(trial.Judgment.Usage.TotalTokens, "tokens", "GPT judge Codex JSONL usage")
            },
            Jev = JevMetrics(trial.Judgment),
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

    private static RoutingQualityMetrics Routing(
        IReadOnlyList<string> expected,
        IReadOnlyList<string> observed,
        RoutingCase routingCase)
    {
        if (routingCase == RoutingCase.Ambiguous)
        {
            return new RoutingQualityMetrics
            {
                Correctness = Unavailable("ratio", "ambiguous routing case is observed but not scored"),
                False = Unavailable("count", "ambiguous routing case is observed but not scored"),
                Missed = Unavailable("count", "ambiguous routing case is observed but not scored")
            };
        }

        if (routingCase == RoutingCase.ShouldNotActivate)
        {
            return new RoutingQualityMetrics
            {
                Correctness = Metric(observed.Count == 0 ? 1 : 0, "ratio", MeasurementKind.Derived, "negative routing expectation"),
                False = Metric(observed.Count, "count", MeasurementKind.Measured, "structured executor observations"),
                Missed = Metric(0, "count", MeasurementKind.Derived, "negative routing expectation")
            };
        }

        if (expected.Count == 0)
            return new RoutingQualityMetrics
            {
                Correctness = Unavailable("ratio", "no routing expectation"),
                False = Unavailable("count", "no routing expectation"),
                Missed = Unavailable("count", "no routing expectation")
            };

        var matches = expected.Intersect(observed, StringComparer.Ordinal).Count();
        return new RoutingQualityMetrics
        {
            Correctness = Metric((decimal)matches / expected.Count, "ratio", MeasurementKind.Derived, "expected/observed set intersection"),
            False = Metric(observed.Except(expected, StringComparer.Ordinal).Count(), "count", MeasurementKind.Derived, "expected/observed set difference"),
            Missed = Metric(expected.Except(observed, StringComparer.Ordinal).Count(), "count", MeasurementKind.Derived, "expected/observed set difference")
        };
    }

    private static NestedDelegationMetrics NestedDelegation(bool? expected, int? observedDepth)
    {
        if (expected is null)
            return new NestedDelegationMetrics
            {
                Outcome = GateOutcome.NotApplicable,
                Correctness = Unavailable("ratio", "nested delegation not requested")
            };
        if (observedDepth is null)
            return new NestedDelegationMetrics
            {
                Outcome = GateOutcome.Fail,
                Correctness = Unavailable("ratio", "executor did not expose delegation depth")
            };
        var observed = observedDepth > 1;
        return new NestedDelegationMetrics
        {
            Outcome = observed == expected ? GateOutcome.Pass : GateOutcome.Fail,
            Correctness = Metric(observed == expected ? 1 : 0, "ratio", MeasurementKind.Derived, "expected nested delegation compared with structured maximum depth")
        };
    }

    private static GateOutcome Compare(bool? expected, bool? observed) => expected is null
        ? GateOutcome.NotApplicable
        : observed is null || observed != expected ? GateOutcome.Fail : GateOutcome.Pass;

    private static JevIntelligenceMetrics JevMetrics(JudgeResult judgment) => new()
    {
        DeterministicResolutions = Metric(judgment.Path == JudgingPath.Deterministic ? 1 : 0, "count", MeasurementKind.Derived, "judge resolution path"),
        PurposeResolutions = Metric(0, "count", MeasurementKind.Derived, "evaluation rubric does not classify purpose"),
        InputResolutions = Metric(0, "count", MeasurementKind.Derived, "evaluation rubric does not classify input selection"),
        CostResolutions = Metric(judgment.JevUsage.RemoteCalls, "calls", MeasurementKind.Measured, "JEV remote-call cost unit"),
        ConfidenceResolutions = Metric(judgment.Path == JudgingPath.Jev ? 1 : 0, "count", MeasurementKind.Derived, "accepted JEV confidence threshold"),
        OutcomeResolutions = Metric(judgment.Path == JudgingPath.Jev ? 1 : 0, "count", MeasurementKind.Derived, "bounded JEV rubric outcome"),
        Fallbacks = Metric(judgment.JevUsage.Fallbacks, "count", MeasurementKind.Measured, "JEV client instrumentation"),
        Escalations = Metric(judgment.Escalated ? 1 : 0, "count", MeasurementKind.Measured, "JEV-to-GPT escalation"),
        FalseExclusions = Unavailable("count", "requires calibration ground truth"),
        ContextAvoided = Unavailable("tokens", "equivalent GPT context was not measured")
    };

    private static StaticCostMetrics StaticCost(EvaluationArmDefinition arm, string planDirectory)
    {
        var skills = ResolveSkillDirectories(arm.SkillPaths, planDirectory).Select(StaticCostAnalyzer.AnalyzeSkill).ToArray();
        var agents = ResolveAgentFiles(arm.AgentPaths, planDirectory).Select(StaticCostAnalyzer.AnalyzeAgent).ToArray();
        return new StaticCostMetrics
        {
            Skill = new SkillStaticCost
            {
                Trigger = Estimated(skills.Select(item => item.Trigger), "skill frontmatter"),
                SkillFile = Estimated(skills.Select(item => item.SkillFile), "SKILL.md"),
                LazyReferences = Estimated(skills.Select(item => item.LazyReferences), "files beneath references/"),
                AlwaysVisible = Estimated(skills.Select(item => item.AlwaysVisible), "skill name and description routing surface"),
                ActivationVisible = Estimated(skills.Select(item => item.ActivationVisible), "activated SKILL.md")
            },
            Agent = new AgentStaticCost
            {
                Configuration = Estimated(agents.Select(item => item.Configuration), "custom-agent TOML excluding developer_instructions"),
                Instructions = Estimated(agents.Select(item => item.Instructions), "custom-agent developer_instructions")
            }
        };
    }

    private static NumericMetric Estimated(IEnumerable<TextCost> costs, string scope)
    {
        var array = costs.ToArray();
        return array.Length == 0
            ? Unavailable("estimated-tokens", $"no configured {scope}")
            : Metric(array.Sum(item => item.Tokens), "estimated-tokens", MeasurementKind.Estimated,
                $"{scope}; {StaticCostAnalyzer.TokenMethod}");
    }

    private static IEnumerable<string> ResolveSkillDirectories(IReadOnlyList<string> paths, string planDirectory) =>
        paths.Select(path => CompatibilityHasher.Resolve(planDirectory, path)).SelectMany(path =>
            File.Exists(Path.Combine(path, "SKILL.md"))
                ? [path]
                : Directory.Exists(path)
                    ? Directory.GetFiles(path, "SKILL.md", SearchOption.AllDirectories).Select(file => Path.GetDirectoryName(file)!)
                    : []);

    private static IEnumerable<string> ResolveAgentFiles(IReadOnlyList<string> paths, string planDirectory) =>
        paths.Select(path => CompatibilityHasher.Resolve(planDirectory, path)).SelectMany(path =>
            File.Exists(path) && Path.GetExtension(path).Equals(".toml", StringComparison.OrdinalIgnoreCase)
                ? [path]
                : Directory.Exists(path) ? Directory.GetFiles(path, "*.toml", SearchOption.AllDirectories) : []);

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
