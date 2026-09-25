using System.Text.Json;

namespace SdevEng.Metrics;

public enum PairwiseOutcome { CandidateA, CandidateB, Tie, Disagreement }

public sealed record PairwiseJudgeResult
{
    public required PairwiseOutcome Outcome { get; init; }
    public required PairwiseOutcome FirstPositionOutcome { get; init; }
    public required PairwiseOutcome SwappedPositionOutcome { get; init; }
    public required ExecutionUsage Usage { get; init; }
    public string? Failure { get; init; }
}

public sealed class PairwiseGptJudge(IEvaluationExecutor executor)
{
    public async Task<PairwiseJudgeResult> JudgeAsync(string rubric, string candidateA, string candidateB,
        RunnerProvider provider, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(provider.Provider, "openai", StringComparison.OrdinalIgnoreCase) ||
            provider.Model.Contains("claude", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Pairwise judging requires an OpenAI/GPT provider and model.", nameof(provider));
        var first = await ExecuteAsync(rubric, candidateA, candidateB, provider, timeout, cancellationToken);
        var swapped = await ExecuteAsync(rubric, candidateB, candidateA, provider, timeout, cancellationToken);
        var firstOutcome = Normalize(first.Payload?.Winner, swapped: false);
        var swappedOutcome = Normalize(swapped.Payload?.Winner, swapped: true);
        var failure = first.Failure ?? swapped.Failure;
        var outcome = failure is not null || firstOutcome != swappedOutcome
            ? PairwiseOutcome.Disagreement : firstOutcome;
        return new PairwiseJudgeResult
        {
            Outcome = outcome,
            FirstPositionOutcome = firstOutcome,
            SwappedPositionOutcome = swappedOutcome,
            Usage = Sum(first.Execution.Usage, swapped.Execution.Usage),
            Failure = failure
        };
    }

    private async Task<PairwisePass> ExecuteAsync(string rubric, string left, string right,
        RunnerProvider provider, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var prompt = "You are a bounded pairwise evaluation judge. Compare only the two candidates against the rubric. " +
            "Return JSON only: {\"winner\":\"left|right|tie\"}. Do not prefer a position.\n" +
            $"Rubric: {rubric}\nLEFT:\n{EvaluationJudge.Bound(left)}\nRIGHT:\n{EvaluationJudge.Bound(right)}";
        var workspace = Path.Combine(Path.GetTempPath(), $"codex-toolkit-pairwise-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        ExecutionResult execution;
        try
        {
            execution = await executor.ExecuteAsync(new ExecutionRequest(
                prompt, workspace, provider with { Provider = "openai", Reasoning = "low" }, timeout, "read-only"), cancellationToken);
        }
        finally { Directory.Delete(workspace, recursive: true); }
        if (execution.Failure is not null) return new PairwisePass(null, execution, execution.Failure);
        try
        {
            var payload = JsonSerializer.Deserialize<PairwisePayload>(execution.Response, EvaluationRecordJson.Options);
            if (payload?.Winner is not ("left" or "right" or "tie")) throw new JsonException("winner must be left, right, or tie.");
            return new PairwisePass(payload, execution, null);
        }
        catch (JsonException exception)
        {
            return new PairwisePass(null, execution, $"Invalid pairwise GPT judge response: {exception.Message}");
        }
    }

    private static PairwiseOutcome Normalize(string? winner, bool swapped) => winner switch
    {
        "left" => swapped ? PairwiseOutcome.CandidateB : PairwiseOutcome.CandidateA,
        "right" => swapped ? PairwiseOutcome.CandidateA : PairwiseOutcome.CandidateB,
        "tie" => PairwiseOutcome.Tie,
        _ => PairwiseOutcome.Disagreement
    };

    private static ExecutionUsage Sum(ExecutionUsage first, ExecutionUsage second) => new()
    {
        InputTokens = Add(first.InputTokens, second.InputTokens),
        OutputTokens = Add(first.OutputTokens, second.OutputTokens),
        TotalTokens = Add(first.TotalTokens, second.TotalTokens),
        Turns = Add(first.Turns, second.Turns),
        ToolCalls = Add(first.ToolCalls, second.ToolCalls)
    };

    private static long? Add(long? first, long? second) => first is null && second is null ? null : (first ?? 0) + (second ?? 0);
    private static int? Add(int? first, int? second) => first is null && second is null ? null : (first ?? 0) + (second ?? 0);
    private sealed record PairwisePayload(string Winner);
    private sealed record PairwisePass(PairwisePayload? Payload, ExecutionResult Execution, string? Failure);
}
