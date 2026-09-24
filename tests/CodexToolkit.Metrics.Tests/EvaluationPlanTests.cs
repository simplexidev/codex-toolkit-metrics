namespace CodexToolkit.Metrics.Tests;

public sealed class EvaluationPlanTests
{
    [Fact]
    public void RoundTripsLowercasePlanEnums()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            RunnerTestSupport.Plan("fixture"), EvaluationRecordJson.Options);

        Assert.Contains("\"path\": \"deterministic\"", json, StringComparison.Ordinal);
        Assert.Contains("\"outputKind\": \"text\"", json, StringComparison.Ordinal);
        Assert.NotNull(System.Text.Json.JsonSerializer.Deserialize<EvaluationPlan>(json, EvaluationRecordJson.Options));
    }

    [Theory]
    [InlineData("anthropic", "claude-sonnet-4")]
    [InlineData("openai", "claude-pretender")]
    public void RejectsNonOpenAiOrClaudeExecutors(string provider, string model)
    {
        var plan = RunnerTestSupport.Plan("fixture") with
        {
            Executor = new RunnerProvider
            {
                Provider = provider,
                Model = model,
                Reasoning = "low",
                Version = "test-cli-v1"
            }
        };

        var errors = EvaluationPlanLoader.Validate(plan);

        Assert.Contains(errors, error =>
            error.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Claude", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RejectsProviderConfigurationOnDeterministicJudge()
    {
        var plan = RunnerTestSupport.Plan("fixture") with
        {
            Judge = new JudgeConfiguration
            {
                Path = JudgingPath.Deterministic,
                Provider = "openai",
                Model = "gpt-5"
            }
        };

        Assert.Contains(EvaluationPlanLoader.Validate(plan), error =>
            error.Contains("only valid for gpt or jev", StringComparison.Ordinal));
    }

    [Fact]
    public void JevJudgeRequiresOpenAiFallbackAndSafeBoundedConfiguration()
    {
        var plan = RunnerTestSupport.Plan("fixture") with
        {
            Judge = new JudgeConfiguration
            {
                Path = JudgingPath.Jev,
                Provider = "anthropic",
                Model = "claude",
                Jev = new JevJudgeConfiguration { ApiUrl = "http://example.invalid", MaxInputBytes = 100 }
            }
        };

        var errors = EvaluationPlanLoader.Validate(plan);
        Assert.Contains(errors, error => error.Contains("OpenAI", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("HTTPS", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("maxInputBytes", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsRawResultsUnderPublicData()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new RawResultStore(Path.Combine(Path.GetTempPath(), "repo", "data", "public", "runs")));
    }
}
