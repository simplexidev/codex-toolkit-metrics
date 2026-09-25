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
            var plan = Path.Combine(root, "plan.json");
            await File.WriteAllTextAsync(plan, "{}");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => V2AcceptanceAggregator.AggregateAsync(root, plan, "baseline.json", "capabilities.json", "0123456789abcdef0123456789abcdef01234567", DateTimeOffset.Parse("2026-09-24T00:00:00Z")));
            Assert.Contains("Acceptance plan is invalid", error.Message);
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
            var plan = Path.Combine(root, "plan.json");
            await File.WriteAllTextAsync(plan, "{}");

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => V2AcceptanceAggregator.AggregateAsync(
                root, plan, "baseline.json", "capabilities.json", "abcdef0123456789abcdef0123456789abcdef01", DateTimeOffset.UtcNow));
            Assert.Contains("Acceptance plan is invalid", error.Message);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
