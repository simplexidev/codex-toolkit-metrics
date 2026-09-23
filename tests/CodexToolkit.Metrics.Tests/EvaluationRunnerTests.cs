using System.Text.Json;

namespace CodexToolkit.Metrics.Tests;

public sealed class EvaluationRunnerTests
{
    [Fact]
    public async Task IsolatesEveryRepetitionAndSerializesPartialMetrics()
    {
        using var environment = new RunnerEnvironment();
        var executor = new RecordingExecutor();
        var plan = RunnerTestSupport.Plan("fixture") with { Repetitions = 2 };
        var runner = new EvaluationRunner(executor, new EvaluationJudge(executor));

        var summary = await runner.RunAsync(plan, environment.Options(reuse: false));

        Assert.Equal(2, executor.Calls.Count);
        Assert.Equal(2, executor.Calls.Select(call => call.Workspace).Distinct().Count());
        Assert.All(executor.Calls, call => Assert.False(Directory.Exists(call.Workspace)));
        Assert.All(executor.SawOriginalFixture, Assert.True);
        Assert.Equal(2, summary.Passed);

        var recordPath = Path.Combine(environment.Raw, plan.Suite, "records", "scenario", "vanilla", "1.json");
        var record = EvaluationRecordJson.Deserialize(await File.ReadAllTextAsync(recordPath));
        Assert.NotNull(record);
        Assert.Equal(MeasurementKind.Unavailable, record.Efficiency.GptInputTokens.Kind);
        Assert.Equal(2, record.Statistics.Summaries[0].Repetitions.Value);
        Assert.True(EvaluationRecordValidator.Validate(record).IsValid);
    }

    [Fact]
    public async Task ReusesValidBaselinesAndRejectsStaleOnBehaviorChange()
    {
        using var environment = new RunnerEnvironment();
        var executor = new RecordingExecutor();
        var runner = new EvaluationRunner(executor, new EvaluationJudge(executor));
        var plan = RunnerTestSupport.Plan("fixture");

        await runner.RunAsync(plan, environment.Options(reuse: false));
        var reused = await runner.RunAsync(plan, environment.Options(reuse: true));

        Assert.Single(executor.Calls);
        Assert.Equal(BaselineDisposition.Reused, Assert.Single(reused.Trials).Baseline);

        var changed = plan with
        {
            Executor = plan.Executor with { Reasoning = "medium" }
        };
        var stale = await runner.RunAsync(changed, environment.Options(reuse: true));

        Assert.Equal(2, executor.Calls.Count);
        Assert.Equal(BaselineDisposition.StaleRejected, Assert.Single(stale.Trials).Baseline);
    }

    [Fact]
    public async Task PersistsTimeoutAndFailureWithoutLosingSummary()
    {
        using var environment = new RunnerEnvironment();
        var executor = new RecordingExecutor
        {
            Result = new ExecutionResult
            {
                ExitCode = null,
                TimedOut = true,
                Response = "",
                StandardOutput = "partial",
                StandardError = "deadline",
                Elapsed = TimeSpan.FromSeconds(2),
                Failure = "Timed out."
            }
        };
        var plan = RunnerTestSupport.Plan("fixture");

        var summary = await new EvaluationRunner(executor, new EvaluationJudge(executor))
            .RunAsync(plan, environment.Options(reuse: false));

        var trial = Assert.Single(summary.Trials);
        Assert.True(trial.TimedOut);
        Assert.False(trial.Passed);
        var rawPath = Path.Combine(environment.Raw, plan.Suite, "trials", "scenario", "vanilla", "1.raw.json");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(rawPath));
        Assert.Equal("perform the fixture task", json.RootElement.GetProperty("prompt").GetString());
        Assert.True(json.RootElement.GetProperty("execution").GetProperty("timedOut").GetBoolean());
        Assert.Equal("deadline", json.RootElement.GetProperty("execution").GetProperty("standardError").GetString());
    }

    [Fact]
    public async Task ConvertsEscapingAssertionPathIntoFailedAssertion()
    {
        using var environment = new RunnerEnvironment();
        var plan = RunnerTestSupport.Plan("fixture") with
        {
            Scenarios =
            [
                RunnerTestSupport.Scenario() with
                {
                    Expected = new ScenarioExpectations { FilesExist = ["../outside"] }
                }
            ]
        };
        var executor = new RecordingExecutor();

        var summary = await new EvaluationRunner(executor, new EvaluationJudge(executor))
            .RunAsync(plan, environment.Options(reuse: false));

        Assert.False(Assert.Single(summary.Trials).Passed);
    }

    private sealed class RecordingExecutor : IEvaluationExecutor
    {
        public List<ExecutionRequest> Calls { get; } = [];
        public List<bool> SawOriginalFixture { get; } = [];
        public ExecutionResult? Result { get; init; }

        public async Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken)
        {
            Calls.Add(request);
            var fixturePath = Path.Combine(request.Workspace, "fixture.txt");
            SawOriginalFixture.Add(await File.ReadAllTextAsync(fixturePath, cancellationToken) == "original");
            await File.WriteAllTextAsync(fixturePath, "mutated", cancellationToken);
            return Result ?? new ExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                Response = "ok",
                StandardOutput = "",
                StandardError = "",
                Elapsed = TimeSpan.FromMilliseconds(10),
                Usage = new ExecutionUsage { ToolCalls = 0 }
            };
        }
    }
}

internal sealed class RunnerEnvironment : IDisposable
{
    public RunnerEnvironment()
    {
        Root = Path.Combine(Path.GetTempPath(), $"metrics-runner-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(Root, "fixture"));
        File.WriteAllText(Path.Combine(Root, "fixture", "fixture.txt"), "original");
        PlanPath = Path.Combine(Root, "plan.json");
        Raw = Path.Combine(Root, "private", "runs");
    }

    public string Root { get; }
    public string PlanPath { get; }
    public string Raw { get; }

    public EvaluationRunOptions Options(bool reuse) => new(PlanPath, Raw, reuse);

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}

internal static class RunnerTestSupport
{
    public static EvaluationPlan Plan(string fixtureRoot) => new()
    {
        SchemaVersion = EvaluationPlan.CurrentSchemaVersion,
        Suite = "test-suite",
        FixtureRoot = fixtureRoot,
        Repetitions = 1,
        TimeoutSeconds = 5,
        Executor = new RunnerProvider
        {
            Provider = "openai",
            Model = "gpt-5",
            Reasoning = "low",
            Version = "test-cli-v1"
        },
        Judge = new JudgeConfiguration { Path = JudgingPath.Deterministic },
        Provenance = new RunProvenance
        {
            Toolkit = new RevisionIdentity { Sha = "abcdef1", Version = "test" },
            Metrics = new RevisionIdentity { Sha = "abcdef2", Version = "test" },
            Upstream = new UpstreamIdentity()
        },
        Arms =
        [
            new EvaluationArmDefinition
            {
                Id = "vanilla",
                Comparison = ComparisonArm.Vanilla,
                Customization = CustomizationArm.BuiltinOrNoCustom
            }
        ],
        Scenarios = [Scenario()]
    };

    public static EvaluationScenario Scenario() => new()
    {
        Id = "scenario",
        Capability = "test",
        Prompt = "perform the fixture task",
        Expected = new ScenarioExpectations
        {
            ExitCode = 0,
            OutputKind = OutputKind.Text,
            ResponseContains = ["ok"]
        }
    };
}
