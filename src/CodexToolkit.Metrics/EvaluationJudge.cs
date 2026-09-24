using System.Text.Json;

namespace CodexToolkit.Metrics;

public interface IEvaluationJudge
{
    Task<JudgeResult> JudgeAsync(
        JudgeConfiguration configuration,
        EvaluationScenario scenario,
        ExecutionResult execution,
        IReadOnlyList<AssertionResult> assertions,
        string workspace,
        RunnerProvider executorConfiguration,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed class EvaluationJudge(IEvaluationExecutor executor) : IEvaluationJudge
{
    public async Task<JudgeResult> JudgeAsync(
        JudgeConfiguration configuration,
        EvaluationScenario scenario,
        ExecutionResult execution,
        IReadOnlyList<AssertionResult> assertions,
        string workspace,
        RunnerProvider executorConfiguration,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (configuration.Path == JudgingPath.Deterministic)
        {
            return new JudgeResult
            {
                Path = JudgingPath.Deterministic,
                Outcome = assertions.All(assertion => assertion.Passed) && execution.Failure is null
                    ? GateOutcome.Pass : GateOutcome.Fail,
                Method = "deterministic assertions"
            };
        }

        if (configuration.Path == JudgingPath.Jev)
        {
            return new JudgeResult
            {
                Path = JudgingPath.Jev,
                Outcome = GateOutcome.NotApplicable,
                Method = "JEV boundary reserved; full semantic judging is deferred"
            };
        }

        var provider = executorConfiguration with
        {
            Provider = configuration.Provider!,
            Model = configuration.Model!,
            Reasoning = "low"
        };
        var prompt = BuildPrompt(scenario, execution.Response, assertions);
        var judgeWorkspace = Path.Combine(Path.GetTempPath(), $"codex-toolkit-judge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(judgeWorkspace);
        ExecutionResult judged;
        try
        {
            judged = await executor.ExecuteAsync(
                new ExecutionRequest(prompt, judgeWorkspace, provider, timeout, "read-only"), cancellationToken);
        }
        finally
        {
            Directory.Delete(judgeWorkspace, recursive: true);
        }
        if (judged.Failure is not null)
        {
            return new JudgeResult
            {
                Path = JudgingPath.Gpt,
                Outcome = GateOutcome.Fail,
                Method = $"OpenAI GPT judge {configuration.Model}",
                Usage = judged.Usage,
                Failure = judged.Failure
            };
        }
        try
        {
            var payload = JsonSerializer.Deserialize<GptJudgmentPayload>(judged.Response, EvaluationRecordJson.Options);
            if (payload is null || payload.Score is < 0 or > 1)
            {
                throw new JsonException("Judge response must contain score in [0,1].");
            }

            return new JudgeResult
            {
                Path = JudgingPath.Gpt,
                Outcome = payload.Pass ? GateOutcome.Pass : GateOutcome.Fail,
                Score = payload.Score,
                Method = $"OpenAI GPT judge {configuration.Model}",
                Usage = judged.Usage,
                Failure = judged.Failure
            };
        }
        catch (JsonException exception)
        {
            return new JudgeResult
            {
                Path = JudgingPath.Gpt,
                Outcome = GateOutcome.Fail,
                Method = $"OpenAI GPT judge {configuration.Model}",
                Usage = judged.Usage,
                Failure = $"Invalid GPT judge response: {exception.Message}"
            };
        }
    }

    private static string BuildPrompt(
        EvaluationScenario scenario,
        string response,
        IReadOnlyList<AssertionResult> assertions)
    {
        var boundedResponse = response.Length <= 20_000 ? response : response[..20_000];
        return "You are a bounded evaluation judge. Evaluate only the supplied response against the " +
            "scenario metadata and deterministic results. Return JSON only: " +
            "{\"pass\":true|false,\"score\":0.0-1.0}.\n" +
            $"Scenario: {scenario.Id}\nCapability: {scenario.Capability}\n" +
            $"Rubric: {scenario.Expected.SemanticRubric ?? "Satisfy the scenario accurately and without unsupported claims."}\n" +
            $"Assertions: {JsonSerializer.Serialize(assertions, EvaluationRecordJson.Options)}\n" +
            $"Response:\n{boundedResponse}";
    }

    private sealed record GptJudgmentPayload(bool Pass, decimal Score);
}
