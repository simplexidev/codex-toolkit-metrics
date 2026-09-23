using System.Text.Json.Nodes;

namespace CodexToolkit.Metrics.Tests;

public sealed class PreOptimizationBaselineAggregatorTests
{
    [Fact]
    public async Task ProducesSanitizedReviewedArmAggregates()
    {
        var root = Path.Combine(Path.GetTempPath(), $"metrics-baseline-{Guid.NewGuid():N}");
        var records = Path.Combine(root, "suite", "records", "scenario", "optimized-new");
        Directory.CreateDirectory(records);
        File.Copy(Fixture("evaluation-valid-v1.json"), Path.Combine(records, "1.json"));

        try
        {
            var json = await PreOptimizationBaselineAggregator.AggregateAsync(
                root,
                "0123456789abcdef0123456789abcdef01234567",
                DateTimeOffset.Parse("2026-09-22T00:00:00Z"));
            var document = JsonNode.Parse(json)!;

            Assert.Equal("reviewed", document["provenance"]!["approval"]!.GetValue<string>());
            Assert.Equal(1, document["provenance"]!["sourceRuns"]!.GetValue<int>());
            Assert.Contains(document["metrics"]!.AsArray(), item =>
                item!["name"]!.GetValue<string>() == "optimized-new-pass-rate");
            Assert.DoesNotContain("prompt", json, StringComparison.OrdinalIgnoreCase);

            var publicPath = Path.Combine(root, "public.json");
            await File.WriteAllTextAsync(publicPath, json);
            var validation = await PublicMetricsValidator.ValidateFileAsync(publicPath);
            Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsRecordsFromAnotherToolkitRevision()
    {
        var root = Path.Combine(Path.GetTempPath(), $"metrics-baseline-{Guid.NewGuid():N}");
        var records = Path.Combine(root, "suite", "records", "scenario", "arm");
        Directory.CreateDirectory(records);
        File.Copy(Fixture("evaluation-valid-v1.json"), Path.Combine(records, "1.json"));

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PreOptimizationBaselineAggregator.AggregateAsync(root, "abcdef1", DateTimeOffset.UtcNow));
            Assert.Contains("do not match", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
