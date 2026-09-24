using System.Text.Json;

namespace CodexToolkit.Metrics.Tests;

public sealed class V2AcceptanceAggregatorTests
{
    [Fact]
    public async Task AggregatePublishesQualityFirstComparisonAndEvidenceCoverage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"codex-metrics-acceptance-{Guid.NewGuid():N}");
        var records = Path.Combine(root, "suite", "records");
        Directory.CreateDirectory(records);
        try
        {
            var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "evaluation-valid-v1.json");
            File.Copy(fixture, Path.Combine(records, "record.json"));
            var baseline = Path.Combine(root, "baseline.json");
            await File.WriteAllTextAsync(baseline, """
                {
                  "scenarios": { "total": 1, "passed": 1 },
                  "metrics": [
                    { "name": "scenario-pass-rate", "value": 1 },
                    { "name": "dotnet-vanilla-pass-rate", "value": 0.8 },
                    { "name": "dotnet-upstream-pass-rate", "value": 0.9 }
                  ]
                }
                """);

            var json = await V2AcceptanceAggregator.AggregateAsync(
                root, baseline, "0123456789abcdef0123456789abcdef01234567", DateTimeOffset.Parse("2026-09-24T00:00:00Z"));
            using var document = JsonDocument.Parse(json);
            var metrics = document.RootElement.GetProperty("metrics").EnumerateArray()
                .ToDictionary(item => item.GetProperty("name").GetString()!, item => item.GetProperty("value").GetDecimal());

            Assert.Equal(1m, metrics["scenario-pass-rate"]);
            Assert.Equal(0m, metrics["regression-count"]);
            Assert.Equal(0.9m, metrics["upstream-skill-pass-rate"]);
            Assert.Equal(1m, metrics["jev-false-exclusion-coverage"]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AggregateRejectsARevisionMismatch()
    {
        var root = Path.Combine(Path.GetTempPath(), $"codex-metrics-acceptance-{Guid.NewGuid():N}");
        var records = Path.Combine(root, "suite", "records");
        Directory.CreateDirectory(records);
        try
        {
            var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "evaluation-valid-v1.json");
            File.Copy(fixture, Path.Combine(records, "record.json"));
            var baseline = Path.Combine(root, "baseline.json");
            await File.WriteAllTextAsync(baseline, "{\"scenarios\":{\"total\":1,\"passed\":1},\"metrics\":[]}");

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => V2AcceptanceAggregator.AggregateAsync(
                root, baseline, "abcdef0123456789abcdef0123456789abcdef01", DateTimeOffset.UtcNow));
            Assert.Contains("do not match", error.Message);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
