namespace CodexToolkit.Metrics.Tests;

public sealed class EvaluationJudgeTests
{
    [Fact]
    public async Task JevBoundaryIsExplicitlyDeferredWithoutCallingExecutor()
    {
        var executor = new StubExecutor("unused");
        var result = await new EvaluationJudge(executor).JudgeAsync(
            new JudgeConfiguration { Path = JudgingPath.Jev },
            RunnerTestSupport.Scenario(),
            SuccessfulExecution("ok"),
            [],
            Path.GetTempPath(),
            RunnerTestSupport.Plan("fixture").Executor,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(GateOutcome.NotApplicable, result.Outcome);
        Assert.Contains("deferred", result.Method, StringComparison.OrdinalIgnoreCase);
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
