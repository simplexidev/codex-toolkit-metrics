using System.Text.Json;

namespace SdevEng.Metrics;

public sealed record CalibrationPlan
{
    public required string SchemaVersion { get; init; }
    public required RunnerProvider GptJudge { get; init; }
    public JevJudgeConfiguration Jev { get; init; } = new();
    public required IReadOnlyList<CalibrationExample> Examples { get; init; }
}

public sealed record CalibrationExample
{
    public required string Id { get; init; }
    public required string Rubric { get; init; }
    public required string Response { get; init; }
    public required bool ExpectedPass { get; init; }
}

public sealed record CalibrationObservation
{
    public required string Id { get; init; }
    public required bool ExpectedPass { get; init; }
    public bool? JevPass { get; init; }
    public decimal? JevScore { get; init; }
    public decimal? JevConfidence { get; init; }
    public required bool GptPass { get; init; }
    public decimal? GptScore { get; init; }
    public required bool Agreement { get; init; }
    public required bool Mismatch { get; init; }
    public required bool Escalation { get; init; }
    public required JevUsage JevUsage { get; init; }
    public required ExecutionUsage GptUsage { get; init; }
}

public sealed record ConfidenceRecommendation
{
    public decimal? MinConfidence { get; init; }
    public required int EvidenceCount { get; init; }
    public required decimal? Accuracy { get; init; }
    public required string Rationale { get; init; }
}

public sealed record CalibrationReport
{
    public const string CurrentSchemaVersion = "1.0";
    public required string SchemaVersion { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required IReadOnlyList<CalibrationObservation> Observations { get; init; }
    public required ConfidenceRecommendation Recommendation { get; init; }
    public int Agreements => Observations.Count(item => item.Agreement);
    public int Disagreements => Observations.Count - Agreements;
    public int Mismatches => Observations.Count(item => item.Mismatch);
    public int Escalations => Observations.Count(item => item.Escalation);
    public int JevRemoteCalls => Observations.Sum(item => item.JevUsage.RemoteCalls);
    public long? GptJudgeTokens => Observations.Any(item => item.GptUsage.TotalTokens is not null)
        ? Observations.Sum(item => item.GptUsage.TotalTokens ?? 0) : null;
}

public sealed class JudgeCalibration(IJevEvaluationClient jevClient, IEvaluationExecutor gptExecutor)
{
    public async Task<CalibrationReport> RunAsync(CalibrationPlan plan, CancellationToken cancellationToken = default)
    {
        ValidatePlan(plan);
        var observations = new List<CalibrationObservation>(plan.Examples.Count);
        foreach (var example in plan.Examples)
        {
            var jev = await jevClient.EvaluateAsync(
                new JevEvaluationRequest(EvaluationJudge.Bound(example.Response), example.Rubric), plan.Jev, cancellationToken);
            var gpt = await JudgeGptAsync(example, plan.GptJudge, cancellationToken);
            bool? jevPass = jev.Score is { } score ? score >= plan.Jev.PassScore : null;
            var agreement = jevPass is { } decision && decision == gpt.Pass;
            observations.Add(new CalibrationObservation
            {
                Id = example.Id,
                ExpectedPass = example.ExpectedPass,
                JevPass = jevPass,
                JevScore = jev.Score,
                JevConfidence = jev.Confidence,
                GptPass = gpt.Pass,
                GptScore = gpt.Score,
                Agreement = agreement,
                Mismatch = jevPass is { } accepted && (accepted != gpt.Pass || accepted != example.ExpectedPass),
                Escalation = jev.Disposition == JevDisposition.Review,
                JevUsage = jev.Usage,
                GptUsage = gpt.Usage
            });
        }
        return new CalibrationReport
        {
            SchemaVersion = CalibrationReport.CurrentSchemaVersion,
            GeneratedAt = DateTimeOffset.UtcNow,
            Observations = observations,
            Recommendation = Recommend(observations)
        };
    }

    public static void ValidatePlan(CalibrationPlan plan)
    {
        if (plan.Examples.Count is < 1 or > 50) throw new InvalidOperationException("Calibration requires 1-50 bounded examples.");
        if (plan.SchemaVersion != "1.0") throw new InvalidOperationException("Calibration schemaVersion must equal '1.0'.");
        if (!string.Equals(plan.GptJudge.Provider, "openai", StringComparison.OrdinalIgnoreCase) ||
            plan.GptJudge.Model.Contains("claude", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Calibration requires an OpenAI/GPT judge.");
        if (plan.Jev.MinConfidence is < 0 or > 1 || plan.Jev.PassScore is < 0 or > 1 ||
            plan.Jev.TimeoutSeconds is < 1 or > 120 || plan.Jev.MaxInputBytes is < 256 or > 65_536)
            throw new InvalidOperationException("Calibration JEV settings are outside bounded limits.");
        foreach (var example in plan.Examples)
        {
            if (string.IsNullOrWhiteSpace(example.Id) || string.IsNullOrWhiteSpace(example.Rubric) || string.IsNullOrWhiteSpace(example.Response))
                throw new InvalidOperationException("Every calibration example requires id, rubric, and response.");
        }
        if (plan.Examples.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != plan.Examples.Count)
            throw new InvalidOperationException("Calibration example ids must be unique.");
    }

    public static ConfidenceRecommendation Recommend(IReadOnlyList<CalibrationObservation> observations)
    {
        var candidates = observations.Where(item => item.JevPass is not null && item.JevConfidence is not null)
            .OrderBy(item => item.JevConfidence).ToArray();
        foreach (var threshold in candidates.Select(item => item.JevConfidence!.Value).Distinct())
        {
            var evidence = candidates.Where(item => item.JevConfidence >= threshold).ToArray();
            if (evidence.Length < 3) continue;
            var accurate = evidence.Count(item => item.JevPass == item.ExpectedPass);
            var accuracy = (decimal)accurate / evidence.Length;
            if (accuracy >= 0.9m)
                return new ConfidenceRecommendation
                {
                    MinConfidence = threshold,
                    EvidenceCount = evidence.Length,
                    Accuracy = accuracy,
                    Rationale = "Lowest observed threshold with at least three examples and >=90% agreement with expected outcomes."
                };
        }
        return new ConfidenceRecommendation
        {
            EvidenceCount = candidates.Length,
            Accuracy = candidates.Length == 0 ? null : (decimal)candidates.Count(item => item.JevPass == item.ExpectedPass) / candidates.Length,
            Rationale = "Insufficient evidence for a threshold recommendation; retain the configured minimum and collect more bounded examples."
        };
    }

    public static string PublicAggregate(CalibrationReport report, string toolkitRevision)
    {
        var resolved = report.Observations.Where(item => item.JevPass is not null).ToArray();
        var metrics = new List<object>
        {
            Metric("calibration-agreement-rate", report.Observations.Count == 0 ? 0 : (decimal)report.Agreements / report.Observations.Count, "ratio", "higher-is-better", "same-example JEV/GPT decisions", "derived"),
            Metric("calibration-disagreements", report.Disagreements, "count", "lower-is-better", "same-example JEV/GPT decisions", "derived"),
            Metric("calibration-mismatches", report.Mismatches, "count", "lower-is-better", "JEV versus GPT or expected outcome", "derived"),
            Metric("calibration-escalations", report.Escalations, "count", "lower-is-better", "unresolved JEV judgments"),
            Metric("jev-judge-remote-calls", report.JevRemoteCalls, "calls", "lower-is-better", "JEV instrumentation")
        };
        if (report.GptJudgeTokens is { } gptTokens)
            metrics.Add(Metric("gpt-judge-total-tokens", gptTokens, "tokens", "lower-is-better", "OpenAI judge usage"));
        if (report.Recommendation.MinConfidence is { } recommendation)
            metrics.Add(Metric("jev-recommended-min-confidence", recommendation, "ratio", "neutral", "evidence-derived calibration threshold", "derived"));
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            generatedAt = report.GeneratedAt,
            subject = new { repository = "simplexidev/codex-toolkit", revision = toolkitRevision },
            scenarios = new { total = report.Observations.Count, passed = resolved.Count(item => item.JevPass == item.ExpectedPass) },
            metrics,
            provenance = new { sourceRuns = report.Observations.Count, sanitized = true, approval = "reviewed" }
        }, EvaluationRecordJson.Options);
    }

    private async Task<(bool Pass, decimal Score, ExecutionUsage Usage)> JudgeGptAsync(
        CalibrationExample example, RunnerProvider provider, CancellationToken cancellationToken)
    {
        var prompt = "You are a bounded evaluation judge. Return JSON only: {\"pass\":true|false,\"score\":0.0-1.0}.\n" +
            $"Rubric: {example.Rubric}\nResponse:\n{EvaluationJudge.Bound(example.Response)}";
        var workspace = Path.Combine(Path.GetTempPath(), $"codex-toolkit-calibration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            var result = await gptExecutor.ExecuteAsync(new ExecutionRequest(prompt, workspace,
                provider with { Provider = "openai", Reasoning = "low" }, TimeSpan.FromSeconds(120), "read-only"), cancellationToken);
            if (result.Failure is not null) throw new InvalidOperationException("GPT calibration judge failed: " + result.Failure);
            var payload = JsonSerializer.Deserialize<CalibrationGptPayload>(result.Response, EvaluationRecordJson.Options);
            if (payload is null || payload.Score is < 0 or > 1) throw new InvalidOperationException("GPT calibration judge returned invalid JSON.");
            return (payload.Pass, payload.Score, result.Usage);
        }
        finally { Directory.Delete(workspace, recursive: true); }
    }

    private static object Metric(string name, decimal value, string unit, string direction, string method, string kind = "measured") =>
        new { name, value, unit, direction, kind, method };
    private sealed record CalibrationGptPayload(bool Pass, decimal Score);
}
