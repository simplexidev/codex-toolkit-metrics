using System.Text.Json;

namespace CodexToolkit.Metrics;

public static class EvaluationRecordValidator
{
    private const long MaximumEvaluationFileBytes = 2_000_000;

    public static async Task<ValidationResult> ValidateFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return new ValidationResult(["Evaluation file does not exist."]);
        }

        if (file.Length > MaximumEvaluationFileBytes)
        {
            return new ValidationResult([$"Evaluation file exceeds {MaximumEvaluationFileBytes} bytes."]);
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var record = EvaluationRecordJson.Deserialize(json);
            return record is null
                ? new ValidationResult(["The evaluation document must be an object."])
                : Validate(record);
        }
        catch (JsonException exception)
        {
            return new ValidationResult([$"Invalid evaluation JSON: {exception.Message}"]);
        }
    }

    public static ValidationResult Validate(EvaluationRecord record)
    {
        var errors = new List<string>();

        if (record.SchemaVersion is not ("1.0.0" or "1.1.0" or EvaluationRecord.CurrentSchemaVersion))
        {
            errors.Add($"$.schemaVersion must equal '1.0.0', '1.1.0', or '{EvaluationRecord.CurrentSchemaVersion}'.");
        }

        ValidateIdentity(record.Identity, errors);
        if (record.Arm is null)
        {
            errors.Add("$.arm is required.");
        }

        ValidateQuality(record.Quality, record.SchemaVersion, errors);
        ValidateMetrics(record.Efficiency, "$.efficiency", errors);
        ValidateMetrics(record.Jev, "$.jev", errors);
        ValidateMetrics(record.StaticCost, "$.staticCost", errors);
        ValidateStatistics(record.Statistics, errors);

        return new ValidationResult(errors);
    }

    private static void ValidateIdentity(EvaluationIdentity? identity, List<string> errors)
    {
        if (identity is null)
        {
            errors.Add("$.identity is required.");
            return;
        }

        RequireText(identity.Scenario, "$.identity.scenario", errors);
        RequireText(identity.Capability, "$.identity.capability", errors);
        RequireText(identity.Arm, "$.identity.arm", errors);
        if (!string.Equals(identity.Executor?.Provider, "openai", StringComparison.OrdinalIgnoreCase))
            errors.Add("$.identity.executor.provider must be 'openai'.");
        RequireText(identity.Executor?.Model, "$.identity.executor.model", errors);
        RequireText(identity.Executor?.Reasoning, "$.identity.executor.reasoning", errors);
        ValidateRevision(identity.Toolkit, "$.identity.toolkit", errors);
        ValidateRevision(identity.Metrics, "$.identity.metrics", errors);

        if (identity.Repetition < 1)
        {
            errors.Add("$.identity.repetition must be at least 1.");
        }

        if (identity.Timestamp == default)
        {
            errors.Add("$.identity.timestamp must be a valid non-default timestamp.");
        }

        if (identity.Judge is null)
        {
            errors.Add("$.identity.judge is required.");
        }
        else if (identity.Judge.Method is JudgeMethod.Gpt or JudgeMethod.Hybrid)
        {
            RequireText(identity.Judge.Model, "$.identity.judge.model", errors);
        }

        if (identity.Upstream is null)
        {
            errors.Add("$.identity.upstream is required.");
        }
        else
        {
            ValidateOptionalSha(identity.Upstream.Sha, "$.identity.upstream.sha", errors);
            if (identity.Upstream.Path is { } path &&
                (Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal)))
            {
                errors.Add("$.identity.upstream.path must be a repository-relative path without traversal.");
            }
        }
    }

    private static void ValidateRevision(RevisionIdentity? revision, string path, List<string> errors)
    {
        if (revision is null)
        {
            errors.Add($"{path} is required.");
            return;
        }

        ValidateSha(revision.Sha, $"{path}.sha", errors);
        RequireText(revision.Version, $"{path}.version", errors);
    }

    private static void ValidateSha(string? sha, string path, List<string> errors)
    {
        if (sha is null || sha.Length is < 7 or > 64 || !sha.All(Uri.IsHexDigit))
        {
            errors.Add($"{path} must be a 7-64 character hexadecimal revision.");
        }
    }

    private static void ValidateOptionalSha(string? sha, string path, List<string> errors)
    {
        if (sha is not null)
        {
            ValidateSha(sha, path, errors);
        }
    }

    private static void ValidateQuality(QualityMetrics? quality, string schemaVersion, List<string> errors)
    {
        if (quality is null)
        {
            errors.Add("$.quality is required.");
            return;
        }

        ValidateMetrics(quality, "$.quality", errors);

        if (schemaVersion is "1.1.0" or "1.2.0")
        {
            if (quality.InvokedTools is null) errors.Add("$.quality.invokedTools is required.");
            if (quality.ContextIsolation is null) errors.Add("$.quality.contextIsolation is required.");
        }

        ValidateAssertionCounts(quality.DeterministicAssertions, "$.quality.deterministicAssertions", errors);
        ValidateAssertionCounts(quality.SafetyAssertions, "$.quality.safetyAssertions", errors);
    }

    private static void ValidateAssertionCounts(AssertionMetrics? assertions, string path, List<string> errors)
    {
        if (assertions?.Total.Value is { } total && assertions.Passed.Value is { } passed && passed > total)
        {
            errors.Add($"{path}.passed.value must not exceed total.value.");
        }
    }

    private static void ValidateStatistics(StatisticsMetrics? statistics, List<string> errors)
    {
        if (statistics is null)
        {
            errors.Add("$.statistics is required.");
            return;
        }

        RequireText(statistics.BaselineCompatibility?.Hash, "$.statistics.baselineCompatibility.hash", errors);
        RequireText(statistics.BaselineCompatibility?.Version, "$.statistics.baselineCompatibility.version", errors);

        if (statistics.Summaries is null || statistics.Summaries.Count == 0)
        {
            errors.Add("$.statistics.summaries must contain at least one statistical summary.");
            return;
        }

        var metricNames = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < statistics.Summaries.Count; index++)
        {
            var summary = statistics.Summaries[index];
            var path = $"$.statistics.summaries[{index}]";
            if (summary is null)
            {
                errors.Add($"{path} is required.");
                continue;
            }

            RequireText(summary.Metric, $"{path}.metric", errors);
            if (!metricNames.Add(summary.Metric))
            {
                errors.Add($"{path}.metric must be unique.");
            }

            ValidateMetrics(summary, path, errors);
            var confidenceInterval = summary.ConfidenceInterval;
            RequireText(confidenceInterval?.Method, $"{path}.confidenceInterval.method", errors);
            RequireText(confidenceInterval?.Justification, $"{path}.confidenceInterval.justification", errors);

            if (summary.Minimum?.Value is { } minimum && summary.Maximum?.Value is { } maximum && minimum > maximum)
            {
                errors.Add($"{path}.minimum.value must not exceed maximum.value.");
            }

            if (confidenceInterval?.Lower?.Value is { } lower &&
                confidenceInterval.Upper?.Value is { } upper && lower > upper)
            {
                errors.Add($"{path}.confidenceInterval.lower.value must not exceed upper.value.");
            }
        }
    }

    private static void ValidateMetrics(object? value, string path, List<string> errors)
    {
        if (value is null)
        {
            errors.Add($"{path} is required.");
            return;
        }

        foreach (var property in value.GetType().GetProperties())
        {
            var propertyValue = property.GetValue(value);
            var propertyPath = $"{path}.{JsonNamingPolicy.CamelCase.ConvertName(property.Name)}";
            if (propertyValue is NumericMetric metric)
            {
                ValidateNumericMetric(metric, propertyPath, errors);
            }
            else if (propertyValue is not null &&
                     property.PropertyType.Namespace == typeof(EvaluationRecord).Namespace &&
                     !property.PropertyType.IsEnum && property.PropertyType != typeof(string))
            {
                ValidateMetrics(propertyValue, propertyPath, errors);
            }
            else if (propertyValue is null && propertyPath is not ("$.quality.invokedTools" or "$.quality.contextIsolation" or
                         "$.efficiency.jevJudgeCalls" or "$.efficiency.gptJudgeInputTokens" or
                         "$.efficiency.gptJudgeOutputTokens" or "$.efficiency.gptJudgeTotalTokens") &&
                     property.PropertyType != typeof(string))
            {
                errors.Add($"{propertyPath} is required.");
            }
        }
    }

    private static void ValidateNumericMetric(NumericMetric metric, string path, List<string> errors)
    {
        if (metric.Kind == MeasurementKind.Unavailable && metric.Value is not null)
        {
            errors.Add($"{path}.value must be null when kind is 'unavailable'.");
        }
        else if (metric.Kind != MeasurementKind.Unavailable && metric.Value is null)
        {
            errors.Add($"{path}.value is required unless kind is 'unavailable'.");
        }

        RequireText(metric.Unit, $"{path}.unit", errors);
        RequireText(metric.Method, $"{path}.method", errors);
    }

    private static void RequireText(string? value, string path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{path} must be a non-empty string.");
        }
    }
}
