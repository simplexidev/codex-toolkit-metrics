using System.Text.Json;

namespace CodexToolkit.Metrics;

public static class PreOptimizationBaselineAggregator
{
    public static async Task<string> AggregateAsync(
        string rawDirectory,
        string toolkitRevision,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken = default)
    {
        if (toolkitRevision.Length is < 7 or > 64 || !toolkitRevision.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Toolkit revision must be a 7-64 character hexadecimal revision.");

        var root = Path.GetFullPath(rawDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var recordFiles = Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(path => path.Replace(Path.DirectorySeparatorChar, '/').Contains("/records/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (recordFiles.Length == 0)
            throw new InvalidOperationException("No private evaluation records were found beneath a records directory.");

        var records = new List<EvaluationRecord>();
        foreach (var path in recordFiles)
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

        var scenarioGroups = records.GroupBy(record => record.Identity.Scenario, StringComparer.Ordinal).ToArray();
        var metrics = new List<PublicBaselineMetric>
        {
            Metric("scenario-pass-rate",
                Ratio(scenarioGroups.Count(group => group.All(record => record.Quality.FinalQualityGate == GateOutcome.Pass)), scenarioGroups.Length),
                "ratio", "higher-is-better", "derived", "scenarios passing across every evaluated arm")
        };

        foreach (var group in records.GroupBy(record => record.Identity.Arm, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var arm = Slug(group.Key);
            var armRecords = group.ToArray();
            metrics.Add(Metric($"{arm}-pass-rate",
                Ratio(armRecords.Count(record => record.Quality.FinalQualityGate == GateOutcome.Pass), armRecords.Length),
                "ratio", "higher-is-better", "measured", "passing reviewed records divided by records for this arm"));
            AddMean(metrics, arm, "total-tokens", armRecords.Select(record => record.Efficiency.GptTotalTokens.Value),
                "tokens", "lower-is-better", "Codex JSONL usage averaged over available records");
            AddMean(metrics, arm, "elapsed-seconds", armRecords.Select(record => record.Efficiency.ElapsedTime.Value),
                "seconds", "lower-is-better", "monotonic elapsed time averaged over reviewed records");
            AddMean(metrics, arm, "tool-calls", armRecords.Select(record => record.Efficiency.ToolCalls.Value),
                "count", "neutral", "structured tool-call observations averaged over available records");
            metrics.Add(Metric($"{arm}-delegation-rate",
                Ratio(armRecords.Count(record => record.Efficiency.Subagents.Value > 0), armRecords.Length),
                "ratio", "neutral", "derived", "records with observed subagents divided by records for this arm"));
        }

        var document = new PublicBaselineDocument(
            "1.0",
            generatedAt,
            new PublicBaselineSubject("simplexidev/codex-toolkit", toolkitRevision.ToLowerInvariant()),
            new PublicBaselineScenarios(
                scenarioGroups.Length,
                scenarioGroups.Count(group => group.All(record => record.Quality.FinalQualityGate == GateOutcome.Pass))),
            metrics,
            new PublicBaselineProvenance(records.Count, true, "reviewed"));
        return JsonSerializer.Serialize(document, EvaluationRecordJson.Options);
    }

    private static void AddMean(
        List<PublicBaselineMetric> metrics,
        string arm,
        string suffix,
        IEnumerable<decimal?> values,
        string unit,
        string direction,
        string method)
    {
        var available = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        if (available.Length == 0) return;
        metrics.Add(Metric($"{arm}-mean-{suffix}", decimal.Round(available.Average(), 4), unit, direction, "derived", method));
    }

    private static decimal Ratio(int numerator, int denominator) =>
        denominator == 0 ? 0 : decimal.Round((decimal)numerator / denominator, 4);

    private static PublicBaselineMetric Metric(
        string name,
        decimal value,
        string unit,
        string direction,
        string kind,
        string method) => new(name, value, unit, direction, kind, method);

    private static string Slug(string value)
    {
        var chars = value.ToLowerInvariant().Select(character =>
            char.IsLetterOrDigit(character) ? character : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record PublicBaselineDocument(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        PublicBaselineSubject Subject,
        PublicBaselineScenarios Scenarios,
        IReadOnlyList<PublicBaselineMetric> Metrics,
        PublicBaselineProvenance Provenance);

    private sealed record PublicBaselineSubject(string Repository, string Revision);
    private sealed record PublicBaselineScenarios(int Total, int Passed);
    private sealed record PublicBaselineMetric(
        string Name,
        decimal Value,
        string Unit,
        string Direction,
        string Kind,
        string Method);
    private sealed record PublicBaselineProvenance(int SourceRuns, bool Sanitized, string Approval);
}

