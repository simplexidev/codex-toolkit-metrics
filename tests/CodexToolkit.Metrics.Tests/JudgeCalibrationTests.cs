using System.Text.Json;

namespace CodexToolkit.Metrics.Tests;

public sealed class JudgeCalibrationTests
{
    [Fact]
    public async Task RunsBothJudgesOnEveryExampleAndRecordsSeparateCosts()
    {
        var jev = new CalibrationJev(
            Accepted(true, .95m), Accepted(false, .92m), Accepted(true, .91m), Review(.4m));
        var gpt = new CalibrationGpt(true, false, true, false);
        var plan = Plan(true, false, true, false);

        var report = await new JudgeCalibration(jev, gpt).RunAsync(plan);

        Assert.Equal(4, jev.Calls);
        Assert.Equal(4, gpt.Calls);
        Assert.Equal(4, report.Agreements);
        Assert.Equal(1, report.Escalations);
        Assert.Equal(4, report.JevRemoteCalls);
        Assert.Equal(40, report.GptJudgeTokens);
        Assert.Equal(.4m, report.Recommendation.MinConfidence);
        Assert.Equal(4, report.Recommendation.EvidenceCount);
    }

    [Fact]
    public async Task PublicAggregateContainsOnlySanitizedCountsAndRates()
    {
        var secretResponse = "private response that must not be published";
        var report = await new JudgeCalibration(new CalibrationJev(Accepted(true, .95m)), new CalibrationGpt(true))
            .RunAsync(Plan(true) with { Examples = [new CalibrationExample { Id = "one", Rubric = "quality", Response = secretResponse, ExpectedPass = true }] });

        var json = JudgeCalibration.PublicAggregate(report, "abcdef1");
        Assert.DoesNotContain(secretResponse, json, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("scenarios").GetProperty("total").GetInt32());
        var path = Path.Combine(Path.GetTempPath(), $"calibration-public-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, json);
            Assert.True((await PublicMetricsValidator.ValidateFileAsync(path)).IsValid);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static CalibrationPlan Plan(params bool[] expected) => new()
    {
        SchemaVersion = "1.0",
        GptJudge = RunnerTestSupport.Plan("fixture").Executor,
        Examples = expected.Select((value, index) => new CalibrationExample
        {
            Id = $"example-{index}",
            Rubric = "quality",
            Response = $"response-{index}",
            ExpectedPass = value
        }).ToArray()
    };

    private static JevEvaluationResult Accepted(bool pass, decimal confidence) => new()
    {
        Disposition = JevDisposition.Accept,
        Score = pass ? .8m : .2m,
        Confidence = confidence,
        Reason = "accepted",
        Usage = new JevUsage { Invocations = 1, RemoteCalls = 1 }
    };

    private static JevEvaluationResult Review(decimal confidence) => new()
    {
        Disposition = JevDisposition.Review,
        Score = .5m,
        Confidence = confidence,
        Reason = "review",
        Usage = new JevUsage { Invocations = 1, RemoteCalls = 1 }
    };

    private sealed class CalibrationJev(params JevEvaluationResult[] results) : IJevEvaluationClient
    {
        private readonly Queue<JevEvaluationResult> _results = new(results);
        public int Calls { get; private set; }
        public Task<JevEvaluationResult> EvaluateAsync(JevEvaluationRequest request, JevJudgeConfiguration configuration, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class CalibrationGpt(params bool[] results) : IEvaluationExecutor
    {
        private readonly Queue<bool> _results = new(results);
        public int Calls { get; private set; }
        public Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            var pass = _results.Dequeue();
            return Task.FromResult(new ExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                Response = $"{{\"pass\":{pass.ToString().ToLowerInvariant()},\"score\":{(pass ? "0.8" : "0.2")}}}",
                StandardOutput = "",
                StandardError = "",
                Elapsed = TimeSpan.Zero,
                Usage = new ExecutionUsage { TotalTokens = 10 }
            });
        }
    }
}
