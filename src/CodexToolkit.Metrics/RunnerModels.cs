namespace CodexToolkit.Metrics;

public sealed record ExecutionRequest(
    string Prompt,
    string Workspace,
    RunnerProvider Provider,
    TimeSpan Timeout,
    string Sandbox = "workspace-write",
    bool ContextIsolated = false);

public sealed record ExecutionResult
{
    public required int? ExitCode { get; init; }
    public required bool TimedOut { get; init; }
    public required string Response { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public ExecutionUsage Usage { get; init; } = new();
    public ExecutionObservations Observations { get; init; } = new();
    public string? Failure { get; init; }
}

public sealed record ExecutionUsage
{
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public int? Turns { get; init; }
    public int? ToolCalls { get; init; }
}

public sealed record ExecutionObservations
{
    public IReadOnlyList<string> ActivatedSkills { get; init; } = [];
    public IReadOnlyList<string> DelegatedAgents { get; init; } = [];
    public IReadOnlyList<string> InvokedTools { get; init; } = [];
    public int? MaximumDelegationDepth { get; init; }
    public bool? ContextIsolated { get; init; }
}

public interface IEvaluationExecutor
{
    Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken);
}

public sealed record AssertionResult(string Name, bool Passed, string Detail);

public enum BaselineDisposition
{
    NotRequested,
    Reused,
    Missing,
    StaleRejected
}

public sealed record RawTrialResult
{
    public required string CompatibilityHash { get; init; }
    public required string CompatibilityVersion { get; init; }
    public required string Scenario { get; init; }
    public required string Arm { get; init; }
    public required int Repetition { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string Prompt { get; init; }
    public required ExecutionResult Execution { get; init; }
    public required IReadOnlyList<AssertionResult> Assertions { get; init; }
    public required JudgeResult Judgment { get; init; }
}

public sealed record JudgeResult
{
    public required JudgingPath Path { get; init; }
    public required GateOutcome Outcome { get; init; }
    public decimal? Score { get; init; }
    public required string Method { get; init; }
    public string? Failure { get; init; }
    public ExecutionUsage Usage { get; init; } = new();
}

public sealed record TrialSummary
{
    public required string Scenario { get; init; }
    public required string Arm { get; init; }
    public required int Repetition { get; init; }
    public required bool Passed { get; init; }
    public required bool TimedOut { get; init; }
    public required BaselineDisposition Baseline { get; init; }
    public required long? TotalTokens { get; init; }
    public required int? ToolCalls { get; init; }
    public required double ElapsedSeconds { get; init; }
}

public sealed record EvaluationRunSummary
{
    public required string Suite { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required IReadOnlyList<TrialSummary> Trials { get; init; }
    public int Passed => Trials.Count(trial => trial.Passed);
    public int Failed => Trials.Count - Passed;
}

public sealed record EvaluationRunOptions(string PlanPath, string RawDirectory, bool ReuseBaseline);
