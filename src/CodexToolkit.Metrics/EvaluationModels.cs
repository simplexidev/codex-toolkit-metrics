using System.Text.Json.Serialization;

namespace CodexToolkit.Metrics;

public sealed record EvaluationRecord
{
    public const string CurrentSchemaVersion = "1.0.0";

    public required string SchemaVersion { get; init; }

    public required EvaluationIdentity Identity { get; init; }

    public required EvaluationArm Arm { get; init; }

    public required QualityMetrics Quality { get; init; }

    public required EfficiencyMetrics Efficiency { get; init; }

    public required JevIntelligenceMetrics Jev { get; init; }

    public required StaticCostMetrics StaticCost { get; init; }

    public required StatisticsMetrics Statistics { get; init; }
}

public sealed record EvaluationIdentity
{
    public required string Scenario { get; init; }

    public required string Capability { get; init; }

    public string? Skill { get; init; }

    public string? Agent { get; init; }

    public required string Arm { get; init; }

    public required ExecutorIdentity Executor { get; init; }

    public required JudgeIdentity Judge { get; init; }

    public required RevisionIdentity Toolkit { get; init; }

    public required RevisionIdentity Metrics { get; init; }

    public required UpstreamIdentity Upstream { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required int Repetition { get; init; }
}

public sealed record ExecutorIdentity
{
    public required string Model { get; init; }

    public required string Reasoning { get; init; }
}

public sealed record JudgeIdentity
{
    public required JudgeMethod Method { get; init; }

    public string? Model { get; init; }
}

public sealed record RevisionIdentity
{
    public required string Sha { get; init; }

    public required string Version { get; init; }
}

public sealed record UpstreamIdentity
{
    public string? Sha { get; init; }

    public string? Path { get; init; }
}

public sealed record EvaluationArm
{
    public required ComparisonArm Comparison { get; init; }

    public required CustomizationArm Customization { get; init; }
}

public sealed record QualityMetrics
{
    public required GateOutcome Completion { get; init; }

    public required AssertionMetrics DeterministicAssertions { get; init; }

    public required AssertionMetrics SafetyAssertions { get; init; }

    public required NumericMetric SemanticRubric { get; init; }

    public required RoutingQualityMetrics Activation { get; init; }

    public required RoutingQualityMetrics Delegation { get; init; }

    public required NestedDelegationMetrics NestedDelegation { get; init; }

    public required GateOutcome FinalQualityGate { get; init; }
}

public sealed record AssertionMetrics
{
    public required NumericMetric Total { get; init; }

    public required NumericMetric Passed { get; init; }

    public required GateOutcome Outcome { get; init; }
}

public sealed record RoutingQualityMetrics
{
    public required NumericMetric Correctness { get; init; }

    public required NumericMetric False { get; init; }

    public required NumericMetric Missed { get; init; }
}

public sealed record NestedDelegationMetrics
{
    public required GateOutcome Outcome { get; init; }

    public required NumericMetric Correctness { get; init; }
}

public sealed record EfficiencyMetrics
{
    public required NumericMetric GptInputTokens { get; init; }

    public required NumericMetric GptOutputTokens { get; init; }

    public required NumericMetric GptTotalTokens { get; init; }

    public required NumericMetric Turns { get; init; }

    public required NumericMetric ToolCalls { get; init; }

    public required NumericMetric FilesInContext { get; init; }

    public required NumericMetric ContextTokens { get; init; }

    public required NumericMetric Subagents { get; init; }

    public required NumericMetric FullBuilds { get; init; }

    public required NumericMetric TargetedBuilds { get; init; }

    public required NumericMetric FullTests { get; init; }

    public required NumericMetric TargetedTests { get; init; }

    public required NumericMetric ElapsedTime { get; init; }
}

public sealed record JevIntelligenceMetrics
{
    public required NumericMetric DeterministicResolutions { get; init; }

    public required NumericMetric PurposeResolutions { get; init; }

    public required NumericMetric InputResolutions { get; init; }

    public required NumericMetric CostResolutions { get; init; }

    public required NumericMetric ConfidenceResolutions { get; init; }

    public required NumericMetric OutcomeResolutions { get; init; }

    public required NumericMetric Fallbacks { get; init; }

    public required NumericMetric Escalations { get; init; }

    public required NumericMetric FalseExclusions { get; init; }

    public required NumericMetric ContextAvoided { get; init; }
}

public sealed record StaticCostMetrics
{
    public required SkillStaticCost Skill { get; init; }

    public required AgentStaticCost Agent { get; init; }
}

public sealed record SkillStaticCost
{
    public required NumericMetric Trigger { get; init; }

    public required NumericMetric SkillFile { get; init; }

    public required NumericMetric LazyReferences { get; init; }

    public required NumericMetric AlwaysVisible { get; init; }

    public required NumericMetric ActivationVisible { get; init; }
}

public sealed record AgentStaticCost
{
    public required NumericMetric Configuration { get; init; }

    public required NumericMetric Instructions { get; init; }
}

public sealed record StatisticsMetrics
{
    public required BaselineCompatibility BaselineCompatibility { get; init; }

    public required IReadOnlyList<StatisticalSummary> Summaries { get; init; }
}

public sealed record BaselineCompatibility
{
    public required string Hash { get; init; }

    public required string Version { get; init; }
}

public sealed record StatisticalSummary
{
    public required string Metric { get; init; }

    public required NumericMetric Repetitions { get; init; }

    public required NumericMetric Mean { get; init; }

    public required NumericMetric Median { get; init; }

    public required NumericMetric Minimum { get; init; }

    public required NumericMetric Maximum { get; init; }

    public required ConfidenceInterval ConfidenceInterval { get; init; }
}

public sealed record ConfidenceInterval
{
    public required NumericMetric Level { get; init; }

    public required NumericMetric Lower { get; init; }

    public required NumericMetric Upper { get; init; }

    public required string Method { get; init; }

    public required string Justification { get; init; }
}

public sealed record NumericMetric
{
    public decimal? Value { get; init; }

    public required string Unit { get; init; }

    public required MeasurementKind Kind { get; init; }

    public required string Method { get; init; }
}

public enum MeasurementKind
{
    [JsonStringEnumMemberName("measured")]
    Measured,
    [JsonStringEnumMemberName("derived")]
    Derived,
    [JsonStringEnumMemberName("estimated")]
    Estimated,
    [JsonStringEnumMemberName("unavailable")]
    Unavailable
}

public enum ComparisonArm
{
    [JsonStringEnumMemberName("VANILLA")]
    Vanilla,
    [JsonStringEnumMemberName("UPSTREAM")]
    Upstream,
    [JsonStringEnumMemberName("OPTIMIZED")]
    Optimized
}

public enum CustomizationArm
{
    [JsonStringEnumMemberName("BUILTIN-or-NO-CUSTOM")]
    BuiltinOrNoCustom,
    [JsonStringEnumMemberName("PREVIOUS-CUSTOM")]
    PreviousCustom,
    [JsonStringEnumMemberName("OPTIMIZED-or-NEW")]
    OptimizedOrNew
}

public enum JudgeMethod
{
    [JsonStringEnumMemberName("deterministic")]
    Deterministic,
    [JsonStringEnumMemberName("gpt")]
    Gpt,
    [JsonStringEnumMemberName("hybrid")]
    Hybrid
}

public enum GateOutcome
{
    [JsonStringEnumMemberName("pass")]
    Pass,
    [JsonStringEnumMemberName("fail")]
    Fail,
    [JsonStringEnumMemberName("not-applicable")]
    NotApplicable
}
