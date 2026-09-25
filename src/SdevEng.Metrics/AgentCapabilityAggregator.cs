using System.Text.Json;

namespace SdevEng.Metrics;

public static class AgentCapabilityAggregator
{
    private static readonly HashSet<string> Dispositions = new(StringComparer.Ordinal)
    {
        "KEEP", "MODIFY", "RETIRE", "REPLACE_WITH_BUILTIN", "ADD", "INSUFFICIENT_EVIDENCE"
    };

    private static readonly HashSet<string> Routes = new(StringComparer.Ordinal)
    {
        "DETERMINISTIC", "JEV", "AGENT", "CUSTOM_AGENT"
    };

    public static async Task<string> AggregateAsync(
        string rawDirectory,
        string recommendationPath,
        string toolkitRevision,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken = default)
    {
        var records = await LoadRecordsAsync(rawDirectory, toolkitRevision, cancellationToken);
        var policy = JsonSerializer.Deserialize<RecommendationPolicy>(
            await File.ReadAllTextAsync(recommendationPath, cancellationToken), EvaluationRecordJson.Options)
            ?? throw new InvalidOperationException("Recommendation policy must be an object.");
        ValidatePolicy(policy);

        var metrics = new List<PublicMetric>();
        foreach (var item in policy.Recommendations)
        {
            var evidence = Select(records, item.Scenario, item.Arms);
            metrics.Add(Metric(
                $"agent-{Slug(item.Role)}-{Slug(item.Disposition)}", 1, "recommendation", "neutral", "derived", item.Rationale));
            AddEvidenceMetrics(metrics, $"agent-{Slug(item.Role)}", evidence);
            AddOperationalCoverage(metrics, $"agent-{Slug(item.Role)}", evidence);
            foreach (var arm in evidence.GroupBy(record => record.Identity.Arm, StringComparer.Ordinal))
                AddEvidenceMetrics(metrics, $"agent-{Slug(item.Role)}-{Slug(arm.Key)}", arm.ToArray());
        }

        foreach (var item in policy.Routing)
        {
            var evidence = Select(records, item.Scenario, item.Arms);
            metrics.Add(Metric(
                $"routing-{Slug(item.Capability)}-{Slug(item.Route)}", 1, "route", "neutral", "derived", item.Rationale));
            AddEvidenceMetrics(metrics, $"routing-{Slug(item.Capability)}", evidence);
            AddOperationalCoverage(metrics, $"routing-{Slug(item.Capability)}", evidence);
        }

        var scenarios = records.GroupBy(record => record.Identity.Scenario, StringComparer.Ordinal).ToArray();
        var document = new PublicDocument(
            "1.0",
            generatedAt,
            new Subject("simplexidev/codex-toolkit", toolkitRevision.ToLowerInvariant()),
            new ScenarioCounts(scenarios.Length,
                scenarios.Count(group => group.All(record => record.Quality.FinalQualityGate == GateOutcome.Pass))),
            metrics,
            new Provenance(records.Count, true, "reviewed"));
        return JsonSerializer.Serialize(document, EvaluationRecordJson.Options);
    }

    private static async Task<IReadOnlyList<EvaluationRecord>> LoadRecordsAsync(
        string rawDirectory,
        string toolkitRevision,
        CancellationToken cancellationToken)
    {
        if (toolkitRevision.Length is < 7 or > 64 || !toolkitRevision.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Toolkit revision must be a 7-64 character hexadecimal revision.");
        var root = Path.GetFullPath(rawDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var paths = Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(path => path.Replace(Path.DirectorySeparatorChar, '/').Contains("/records/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (paths.Length == 0) throw new InvalidOperationException("No private evaluation records were found beneath a records directory.");

        var records = new List<EvaluationRecord>();
        foreach (var path in paths)
        {
            var record = EvaluationRecordJson.Deserialize(await File.ReadAllTextAsync(path, cancellationToken))
                ?? throw new InvalidOperationException("An evaluation record was empty or null.");
            var validation = EvaluationRecordValidator.Validate(record);
            if (!validation.IsValid)
                throw new InvalidOperationException("A private evaluation record was invalid: " + string.Join("; ", validation.Errors));
            if (!string.Equals(record.Identity.Toolkit.Sha, toolkitRevision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Evaluation records and requested toolkit revision do not match.");
            records.Add(record);
        }
        return records;
    }

    private static void ValidatePolicy(RecommendationPolicy policy)
    {
        if (policy.SchemaVersion != "1.0") throw new InvalidOperationException("Recommendation policy schemaVersion must equal '1.0'.");
        if (policy.Recommendations.Count == 0) throw new InvalidOperationException("At least one agent recommendation is required.");
        foreach (var item in policy.Recommendations)
        {
            if (!Dispositions.Contains(item.Disposition))
                throw new InvalidOperationException($"Unsupported recommendation disposition: {item.Disposition}.");
            ValidateEvidence(item.Role, item.Scenario, item.Arms, item.Rationale);
        }
        foreach (var item in policy.Routing)
        {
            if (!Routes.Contains(item.Route)) throw new InvalidOperationException($"Unsupported capability route: {item.Route}.");
            ValidateEvidence(item.Capability, item.Scenario, item.Arms, item.Rationale);
        }
    }

    private static void ValidateEvidence(string name, string scenario, IReadOnlyList<string> arms, string rationale)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(scenario) || arms.Count == 0)
            throw new InvalidOperationException("Every recommendation and route requires a name, scenario, and evidence arms.");
        if (string.IsNullOrWhiteSpace(rationale) || rationale.Length > 200)
            throw new InvalidOperationException("Every rationale must contain 1-200 characters.");
    }

    private static IReadOnlyList<EvaluationRecord> Select(
        IReadOnlyList<EvaluationRecord> records,
        string scenario,
        IReadOnlyList<string> arms)
    {
        var selected = records.Where(record => record.Identity.Scenario == scenario && arms.Contains(record.Identity.Arm, StringComparer.Ordinal)).ToArray();
        var missing = arms.Except(selected.Select(record => record.Identity.Arm), StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Recommendation evidence for scenario '{scenario}' is missing arms: {string.Join(", ", missing)}.");
        return selected;
    }

    private static void AddEvidenceMetrics(List<PublicMetric> metrics, string prefix, IReadOnlyList<EvaluationRecord> records)
    {
        metrics.Add(Metric($"{prefix}-completion-rate",
            Ratio(records.Count(record => record.Quality.Completion == GateOutcome.Pass), records.Count),
            "ratio", "higher-is-better", "derived", "completed evidence records divided by referenced records"));
        metrics.Add(Metric($"{prefix}-quality-pass-rate",
            Ratio(records.Count(record => record.Quality.FinalQualityGate == GateOutcome.Pass), records.Count),
            "ratio", "higher-is-better", "derived", "passing evidence records divided by referenced records"));
        AddMean(metrics, prefix, "semantic-score", records.Select(record => record.Quality.SemanticRubric.Value), "ratio", "higher-is-better");
        AddMean(metrics, prefix, "total-tokens", records.Select(record => record.Efficiency.GptTotalTokens.Value), "tokens", "lower-is-better");
        AddMean(metrics, prefix, "tool-calls", records.Select(record => record.Efficiency.ToolCalls.Value), "count", "neutral");
        AddMean(metrics, prefix, "elapsed-seconds", records.Select(record => record.Efficiency.ElapsedTime.Value), "seconds", "lower-is-better");
        metrics.Add(Metric($"{prefix}-context-isolation-rate",
            Ratio(records.Count(record => record.Quality.ContextIsolation == GateOutcome.Pass), records.Count),
            "ratio", "higher-is-better", "derived", "isolated evidence records divided by referenced records"));
    }

    private static void AddOperationalCoverage(
        List<PublicMetric> metrics,
        string prefix,
        IReadOnlyList<EvaluationRecord> records)
    {
        metrics.Add(Metric($"{prefix}-skill-measurement-coverage",
            Ratio(records.Count(record => record.Quality.Activation.Correctness.Value.HasValue), records.Count),
            "ratio", "higher-is-better", "derived", "records with scored invoked-skill activation divided by referenced records"));
        metrics.Add(Metric($"{prefix}-delegation-measurement-coverage",
            Ratio(records.Count(record => record.Quality.Delegation.Correctness.Value.HasValue), records.Count),
            "ratio", "higher-is-better", "derived", "records with scored delegation accuracy divided by referenced records"));
        metrics.Add(Metric($"{prefix}-nested-delegation-coverage",
            Ratio(records.Count(record => record.Quality.NestedDelegation.Outcome != GateOutcome.NotApplicable), records.Count),
            "ratio", "higher-is-better", "derived", "records with a nested-delegation expectation divided by referenced records"));
        metrics.Add(Metric($"{prefix}-invoked-tools-coverage",
            Ratio(records.Count(record => record.Quality.InvokedTools?.Correctness.Value.HasValue == true), records.Count),
            "ratio", "higher-is-better", "derived", "records with scored invoked-tool expectations divided by referenced records"));
        AddMean(metrics, prefix, "subagents", records.Select(record => record.Efficiency.Subagents.Value), "count", "neutral");
    }

    private static void AddMean(
        List<PublicMetric> metrics,
        string prefix,
        string suffix,
        IEnumerable<decimal?> values,
        string unit,
        string direction)
    {
        var available = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        if (available.Length == 0) return;
        metrics.Add(Metric($"{prefix}-mean-{suffix}", decimal.Round(available.Average(), 4), unit, direction,
            "derived", "arithmetic mean over referenced reviewed records"));
    }

    private static decimal Ratio(int numerator, int denominator) => denominator == 0
        ? 0 : decimal.Round((decimal)numerator / denominator, 4);

    private static PublicMetric Metric(string name, decimal value, string unit, string direction, string kind, string method) =>
        new(name, value, unit, direction, kind, method);

    private static string Slug(string value) => string.Join('-', value.ToLowerInvariant()
        .Select(character => char.IsLetterOrDigit(character) ? character : '-')
        .Aggregate(new System.Text.StringBuilder(), (builder, character) => builder.Append(character))
        .ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));

    private sealed record RecommendationPolicy(
        string SchemaVersion,
        IReadOnlyList<AgentRecommendation> Recommendations,
        IReadOnlyList<RoutingRecommendation> Routing);
    private sealed record AgentRecommendation(
        string Role, string Disposition, string Scenario, IReadOnlyList<string> Arms, string Rationale);
    private sealed record RoutingRecommendation(
        string Capability, string Route, string Scenario, IReadOnlyList<string> Arms, string Rationale);
    private sealed record PublicDocument(
        string SchemaVersion, DateTimeOffset GeneratedAt, Subject Subject, ScenarioCounts Scenarios,
        IReadOnlyList<PublicMetric> Metrics, Provenance Provenance);
    private sealed record Subject(string Repository, string Revision);
    private sealed record ScenarioCounts(int Total, int Passed);
    private sealed record PublicMetric(string Name, decimal Value, string Unit, string Direction, string Kind, string Method);
    private sealed record Provenance(int SourceRuns, bool Sanitized, string Approval);
}
