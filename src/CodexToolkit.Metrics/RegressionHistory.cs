using System.Text.Json;

namespace CodexToolkit.Metrics;

public static class RegressionHistory
{
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
    {
        "quality", "tokens", "tools", "files-context", "validation-breadth", "routing", "delegation", "jev", "capability-coverage", "measurement-noise", "insufficient-sample"
    };

    public static RegressionHistoryDocument Compare(PublicMetricsSnapshot current, PublicMetricsSnapshot? baseline, RegressionPolicy policy)
    {
        var baselineMetrics = baseline?.Metrics.ToDictionary(x => x.Name, StringComparer.Ordinal) ?? [];
        var gates = new List<RegressionGate>();
        var deltas = new List<RegressionDelta>();
        foreach (var metric in current.Metrics)
        {
            var rule = policy.Rules.FirstOrDefault(x => x.Metric == metric.Name);
            if (rule is null) continue;
            var hasBaseline = baselineMetrics.TryGetValue(metric.Name, out var prior);
            var samples = Math.Min(current.Provenance.SourceRuns, baseline?.Provenance.SourceRuns ?? 0);
            var category = samples < rule.MinimumSamples ? "insufficient-sample" : rule.Category;
            decimal? delta = hasBaseline ? decimal.Round(metric.Value - prior!.Value, 6) : null;
            var regression = hasBaseline && samples >= rule.MinimumSamples && IsRegression(delta!.Value, rule.Direction, rule.Threshold);
            deltas.Add(new RegressionDelta(metric.Name, category, delta, regression ? "regression" : hasBaseline ? "stable" : "unavailable", samples));
            gates.Add(new RegressionGate(metric.Name, rule.Category, metric.Value, rule.Direction, rule.Threshold,
                MeetsGate(metric.Value, rule.Direction, rule.Gate), category == "insufficient-sample" ? "insufficient-sample" : regression ? "fail" : "pass"));
        }
        return new RegressionHistoryDocument("1.0", current.GeneratedAt, current.Subject,
            new RegressionLineage(baseline?.Subject.Revision, baseline?.GeneratedAt, current.UpstreamRevision, current.MetricsRevision),
            current.AcceptedBaseline, gates, deltas, current.Trends ?? [], current.Provenance);
    }

    public static async Task<RegressionHistoryDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var value = JsonSerializer.Deserialize<RegressionHistoryDocument>(await File.ReadAllTextAsync(path, cancellationToken), EvaluationRecordJson.Options)
            ?? throw new InvalidOperationException("Regression history must be an object.");
        Validate(value);
        return value;
    }

    public static void Validate(RegressionHistoryDocument document)
    {
        if (document.SchemaVersion != "1.0") throw new InvalidOperationException("Regression history schemaVersion must equal '1.0'.");
        if (document.Subject.Repository != "simplexidev/codex-toolkit") throw new InvalidOperationException("Regression history has an invalid subject.");
        if (!document.Provenance.Sanitized) throw new InvalidOperationException("Regression history must be sanitized.");
        foreach (var item in document.Gates.Concat(document.Deltas.Select(x => new RegressionGate(x.Metric, x.Category, 0, "neutral", 0, true, x.Status))))
            if (!Categories.Contains(item.Category)) throw new InvalidOperationException($"Unsupported regression category: {item.Category}.");
    }

    private static bool IsRegression(decimal delta, string direction, decimal threshold) => direction switch
    {
        "higher-is-better" => delta < -threshold,
        "lower-is-better" => delta > threshold,
        _ => false
    };
    private static bool MeetsGate(decimal value, string direction, decimal gate) => direction switch
    {
        "higher-is-better" => value >= gate,
        "lower-is-better" => value <= gate,
        _ => true
    };
}

public sealed record PublicMetricsSnapshot(
    DateTimeOffset GeneratedAt, RegressionSubject Subject, IReadOnlyList<RegressionMetric> Metrics,
    RegressionProvenance Provenance, bool AcceptedBaseline = false, string? UpstreamRevision = null,
    string? MetricsRevision = null, IReadOnlyList<RegressionTrend>? Trends = null);
public sealed record RegressionPolicy(IReadOnlyList<RegressionRule> Rules);
public sealed record RegressionRule(string Metric, string Category, string Direction, decimal Threshold, decimal Gate, int MinimumSamples = 2);
public sealed record RegressionHistoryDocument(string SchemaVersion, DateTimeOffset GeneratedAt, RegressionSubject Subject,
    RegressionLineage Lineage, bool AcceptedBaseline, IReadOnlyList<RegressionGate> Gates, IReadOnlyList<RegressionDelta> Deltas,
    IReadOnlyList<RegressionTrend> Trends, RegressionProvenance Provenance);
public sealed record RegressionSubject(string Repository, string Revision);
public sealed record RegressionLineage(string? BaselineRevision, DateTimeOffset? BaselineMeasuredAt, string? UpstreamRevision, string? MetricsRevision);
public sealed record RegressionMetric(string Name, decimal Value);
public sealed record RegressionProvenance(int SourceRuns, bool Sanitized, string Approval);
public sealed record RegressionGate(string Metric, string Category, decimal Value, string Direction, decimal Threshold, bool MeetsThreshold, string Status);
public sealed record RegressionDelta(string Metric, string Category, decimal? Value, string Status, int Samples);
public sealed record RegressionTrend(string Metric, string Direction, decimal Slope, int Samples, string Status);
