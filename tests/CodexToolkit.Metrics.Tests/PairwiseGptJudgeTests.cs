namespace CodexToolkit.Metrics.Tests;

public sealed class PairwiseGptJudgeTests
{
    [Theory]
    [InlineData("left", "right", PairwiseOutcome.CandidateA)]
    [InlineData("tie", "tie", PairwiseOutcome.Tie)]
    [InlineData("left", "left", PairwiseOutcome.Disagreement)]
    public async Task SwapsPositionsAndRepresentsConsensusTiesAndDisagreement(
        string firstWinner, string secondWinner, PairwiseOutcome expected)
    {
        var executor = new QueueExecutor(firstWinner, secondWinner);
        var result = await new PairwiseGptJudge(executor).JudgeAsync(
            "prefer accuracy", "candidate A", "candidate B", RunnerTestSupport.Plan("fixture").Executor,
            TimeSpan.FromSeconds(1));

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(2, executor.Requests.Count);
        Assert.Contains("LEFT:\ncandidate A", executor.Requests[0].Prompt, StringComparison.Ordinal);
        Assert.Contains("LEFT:\ncandidate B", executor.Requests[1].Prompt, StringComparison.Ordinal);
        Assert.Equal(30, result.Usage.TotalTokens);
    }

    private sealed class QueueExecutor(params string[] winners) : IEvaluationExecutor
    {
        private readonly Queue<string> _winners = new(winners);
        public List<ExecutionRequest> Requests { get; } = [];
        public Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new ExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                Response = $"{{\"winner\":\"{_winners.Dequeue()}\"}}",
                StandardOutput = "",
                StandardError = "",
                Elapsed = TimeSpan.Zero,
                Usage = new ExecutionUsage { TotalTokens = 15 }
            });
        }
    }
}
