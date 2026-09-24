namespace CodexToolkit.Metrics.Tests;

public sealed class EvaluationJudgeTests
{
    [Fact]
    public async Task AcceptedJevJudgmentAvoidsGptExecutor()
    {
        var executor = new StubExecutor("unused");
        var jev = new StubJev(new JevEvaluationResult
        {
            Disposition = JevDisposition.Accept,
            Score = 0.75m,
            Confidence = 0.91m,
            Reason = "accepted",
            Usage = new JevUsage { Invocations = 1, RemoteCalls = 1 }
        });
        var result = await new EvaluationJudge(executor, jev).JudgeAsync(
            JevConfiguration(),
            SemanticScenario(),
            SuccessfulExecution("ok"),
            [],
            Path.GetTempPath(),
            RunnerTestSupport.Plan("fixture").Executor,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(GateOutcome.Pass, result.Outcome);
        Assert.Equal(JudgingPath.Jev, result.Path);
        Assert.Equal(0.91m, result.Confidence);
        Assert.Equal(1, result.JevUsage.RemoteCalls);
        Assert.Equal(0, executor.Calls);
    }

    [Fact]
    public async Task UncertainJevJudgmentEscalatesToGpt()
    {
        var executor = new StubExecutor("{\"pass\":true,\"score\":0.8}");
        var jev = new StubJev(new JevEvaluationResult
        {
            Disposition = JevDisposition.Review,
            Score = 0.6m,
            Confidence = 0.4m,
            Reason = "low confidence",
            Usage = new JevUsage { Invocations = 1, RemoteCalls = 1 }
        });
        var result = await new EvaluationJudge(executor, jev).JudgeAsync(
            JevConfiguration(),
            SemanticScenario(), SuccessfulExecution("ok"), [], Path.GetTempPath(),
            RunnerTestSupport.Plan("fixture").Executor, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(result.Escalated);
        Assert.Equal(JudgingPath.Gpt, result.Path);
        Assert.Equal(1, executor.Calls);
        Assert.Contains("escalated", result.Method, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedDeterministicAssertionStopsBeforeSemanticJudges()
    {
        var executor = new StubExecutor("unused");
        var jev = new StubJev(new JevEvaluationResult { Disposition = JevDisposition.Accept, Reason = "unused" });
        var result = await new EvaluationJudge(executor, jev).JudgeAsync(
            JevConfiguration(),
            SemanticScenario(), SuccessfulExecution("ok"), [new AssertionResult("exact", false, "failed")],
            Path.GetTempPath(), RunnerTestSupport.Plan("fixture").Executor, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(JudgingPath.Deterministic, result.Path);
        Assert.Equal(GateOutcome.Fail, result.Outcome);
        Assert.Equal(0, jev.Calls);
        Assert.Equal(0, executor.Calls);
    }

    [Fact]
    public async Task GptBoundaryAcceptsBoundedStructuredJudgment()
    {
        var executor = new StubExecutor("{\"pass\":true,\"score\":0.8}");
        var result = await new EvaluationJudge(executor).JudgeAsync(
            new JudgeConfiguration { Path = JudgingPath.Gpt, Provider = "openai", Model = "gpt-5-mini" },
            RunnerTestSupport.Scenario(),
            SuccessfulExecution("candidate response"),
            [new AssertionResult("example", true, "ok")],
            Path.GetTempPath(),
            RunnerTestSupport.Plan("fixture").Executor,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(GateOutcome.Pass, result.Outcome);
        Assert.Equal(0.8m, result.Score);
        Assert.Equal(1, executor.Calls);
        Assert.Equal("gpt-5-mini", executor.LastRequest!.Provider.Model);
        Assert.Equal("read-only", executor.LastRequest.Sandbox);
        Assert.False(Directory.Exists(executor.LastRequest.Workspace));
    }

    private static ExecutionResult SuccessfulExecution(string response) => new()
    {
        ExitCode = 0,
        TimedOut = false,
        Response = response,
        StandardOutput = "",
        StandardError = "",
        Elapsed = TimeSpan.Zero
    };

    private static EvaluationScenario SemanticScenario() => RunnerTestSupport.Scenario() with
    {
        Expected = RunnerTestSupport.Scenario().Expected with { SemanticRubric = "Answer accurately." }
    };

    private static JudgeConfiguration JevConfiguration() => new()
    {
        Path = JudgingPath.Jev,
        Provider = "openai",
        Model = "gpt-5-mini",
        Jev = new JevJudgeConfiguration()
    };

    private sealed class StubJev(JevEvaluationResult result) : IJevEvaluationClient
    {
        public int Calls { get; private set; }
        public Task<JevEvaluationResult> EvaluateAsync(JevEvaluationRequest request, JevJudgeConfiguration configuration, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class StubExecutor(string response) : IEvaluationExecutor
    {
        public int Calls { get; private set; }
        public ExecutionRequest? LastRequest { get; private set; }

        public Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(SuccessfulExecution(response));
        }
    }
}
