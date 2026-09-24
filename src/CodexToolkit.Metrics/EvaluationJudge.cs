using System.Text.Json;

namespace CodexToolkit.Metrics;

public interface IEvaluationJudge
{
    Task<JudgeResult> JudgeAsync(JudgeConfiguration configuration, EvaluationScenario scenario,
        ExecutionResult execution, IReadOnlyList<AssertionResult> assertions, string workspace,
        RunnerProvider executorConfiguration, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class EvaluationJudge(IEvaluationExecutor executor, IJevEvaluationClient? jevClient = null) : IEvaluationJudge
{
    public async Task<JudgeResult> JudgeAsync(JudgeConfiguration configuration, EvaluationScenario scenario,
        ExecutionResult execution, IReadOnlyList<AssertionResult> assertions, string workspace,
        RunnerProvider executorConfiguration, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deterministicPass = assertions.All(assertion => assertion.Passed) && execution.Failure is null;
        if (!deterministicPass || configuration.Path == JudgingPath.Deterministic ||
            (configuration.Path == JudgingPath.Jev && string.IsNullOrWhiteSpace(scenario.Expected.SemanticRubric)))
        {
            return new JudgeResult
            {
                Path = JudgingPath.Deterministic,
                Outcome = deterministicPass ? GateOutcome.Pass : GateOutcome.Fail,
                Method = "deterministic assertions"
            };
        }

        JevEvaluationResult? jev = null;
        if (configuration.Path == JudgingPath.Jev)
        {
            jev = jevClient is null
                ? new JevEvaluationResult
                {
                    Disposition = JevDisposition.Review,
                    Reason = "JEV client unavailable",
                    Usage = new JevUsage { Invocations = 1, Fallbacks = 1 }
                }
                : await jevClient.EvaluateAsync(
                    new JevEvaluationRequest(Bound(execution.Response), scenario.Expected.SemanticRubric!),
                    configuration.Jev!, cancellationToken);
            if (jev.Disposition == JevDisposition.Accept && jev.Score is { } jevScore)
            {
                return new JudgeResult
                {
                    Path = JudgingPath.Jev,
                    Outcome = jevScore >= configuration.Jev!.PassScore ? GateOutcome.Pass : GateOutcome.Fail,
                    Score = jevScore,
                    Confidence = jev.Confidence,
                    Method = $"bounded JEV rubric {configuration.Jev.Model}",
                    JevUsage = jev.Usage
                };
            }
        }

        var gpt = await JudgeWithGptAsync(configuration, scenario, execution, assertions,
            executorConfiguration, timeout, cancellationToken);
        return gpt with
        {
            JevUsage = jev?.Usage ?? new JevUsage(),
            Escalated = jev is not null,
            Method = jev is null ? gpt.Method : $"JEV review escalated to {gpt.Method}"
        };
    }

    private async Task<JudgeResult> JudgeWithGptAsync(JudgeConfiguration configuration,
        EvaluationScenario scenario, ExecutionResult execution, IReadOnlyList<AssertionResult> assertions,
        RunnerProvider executorConfiguration, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var provider = executorConfiguration with
        {
            Provider = configuration.Provider!,
            Model = configuration.Model!,
            Reasoning = "low"
        };
        var judged = await ExecuteJudgeAsync(BuildPrompt(scenario, execution.Response, assertions),
            provider, timeout, cancellationToken);
        if (judged.Failure is not null) return Failure(configuration, judged, judged.Failure);
        try
        {
            var payload = JsonSerializer.Deserialize<GptJudgmentPayload>(judged.Response, EvaluationRecordJson.Options);
            if (payload is null || payload.Score is < 0 or > 1)
                throw new JsonException("Judge response must contain score in [0,1].");
            return new JudgeResult
            {
                Path = JudgingPath.Gpt,
                Outcome = payload.Pass ? GateOutcome.Pass : GateOutcome.Fail,
                Score = payload.Score,
                Method = $"OpenAI GPT judge {configuration.Model}",
                Usage = judged.Usage
            };
        }
        catch (JsonException exception)
        {
            return Failure(configuration, judged, $"Invalid GPT judge response: {exception.Message}");
        }
    }

    private async Task<ExecutionResult> ExecuteJudgeAsync(string prompt, RunnerProvider provider,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        var judgeWorkspace = Path.Combine(Path.GetTempPath(), $"codex-toolkit-judge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(judgeWorkspace);
        try
        {
            return await executor.ExecuteAsync(new ExecutionRequest(
                prompt, judgeWorkspace, provider, timeout, "read-only"), cancellationToken);
        }
        finally { Directory.Delete(judgeWorkspace, recursive: true); }
    }

    private static JudgeResult Failure(JudgeConfiguration configuration, ExecutionResult execution, string failure) => new()
    {
        Path = JudgingPath.Gpt,
        Outcome = GateOutcome.Fail,
        Method = $"OpenAI GPT judge {configuration.Model}",
        Usage = execution.Usage,
        Failure = failure
    };

    internal static string BuildPrompt(EvaluationScenario scenario, string response,
        IReadOnlyList<AssertionResult> assertions) =>
        "You are a bounded evaluation judge. Evaluate only the supplied response against the " +
        "scenario metadata and deterministic results. Return JSON only: " +
        "{\"pass\":true|false,\"score\":0.0-1.0}.\n" +
        $"Scenario: {scenario.Id}\nCapability: {scenario.Capability}\n" +
        $"Rubric: {scenario.Expected.SemanticRubric ?? "Satisfy the scenario accurately and without unsupported claims."}\n" +
        $"Assertions: {JsonSerializer.Serialize(assertions, EvaluationRecordJson.Options)}\n" +
        $"Response:\n{Bound(response)}";

    internal static string Bound(string value) => value.Length <= 20_000 ? value : value[..20_000];

    private sealed record GptJudgmentPayload(bool Pass, decimal Score);
}
