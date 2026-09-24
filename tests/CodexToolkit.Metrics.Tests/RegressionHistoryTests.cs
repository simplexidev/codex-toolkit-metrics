namespace CodexToolkit.Metrics.Tests;

public sealed class RegressionHistoryTests
{
    [Fact]
    public void FlagsDeterministicQualityRegressionWhenSamplesAreSufficient()
    {
        var result = RegressionHistory.Compare(Snapshot("abcdef1", 0.80m, 3), Snapshot("0123456", 0.95m, 3), new RegressionPolicy([
            new RegressionRule("scenario-pass-rate", "quality", "higher-is-better", 0.05m, 0.9m)]));
        Assert.Equal("fail", result.Gates.Single().Status);
        Assert.Equal("regression", result.Deltas.Single().Status);
    }

    [Fact]
    public void LabelsUnderpoweredComparisonWithoutInventingRegression()
    {
        var result = RegressionHistory.Compare(Snapshot("abcdef1", 0.5m, 1), Snapshot("0123456", 1m, 1),
            new RegressionPolicy([new RegressionRule("scenario-pass-rate", "quality", "higher-is-better", 0.05m, 0.9m)]));
        Assert.Equal("insufficient-sample", result.Deltas.Single().Category);
        Assert.NotEqual("regression", result.Deltas.Single().Status);
    }

    [Fact]
    public async Task RejectsUnsanitizedHistoryFields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"history-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """
            { "schemaVersion":"1.0", "generatedAt":"2026-09-24T00:00:00Z",
              "subject":{"repository":"simplexidev/codex-toolkit","revision":"0123456"},
              "lineage":{"baselineRevision":null,"baselineMeasuredAt":null,"upstreamRevision":null,"metricsRevision":null},
              "acceptedBaseline":true,"gates":[],"deltas":[],"trends":[],
              "provenance":{"sourceRuns":1,"sanitized":true,"approval":"synthetic"},"prompt":"private" }
            """);
        try
        {
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => RegressionHistory.LoadAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static PublicMetricsSnapshot Snapshot(string revision, decimal value, int runs) => new(
        DateTimeOffset.Parse("2026-09-24T00:00:00Z"), new RegressionSubject("simplexidev/codex-toolkit", revision),
        [new RegressionMetric("scenario-pass-rate", value)], new RegressionProvenance(runs, true, "synthetic"));
}
