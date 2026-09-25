using System.Text.Json.Nodes;

namespace SdevEng.Metrics.Tests;

public sealed class AgentCapabilityAggregatorTests
{
    [Fact]
    public async Task PublishesOnlySanitizedEvidenceAndReviewedRecommendations()
    {
        var root = Path.Combine(Path.GetTempPath(), $"metrics-agent-aggregate-{Guid.NewGuid():N}");
        var records = Path.Combine(root, "suite", "records", "structured-help", "optimized-new");
        Directory.CreateDirectory(records);
        File.Copy(Fixture("evaluation-valid-v1.json"), Path.Combine(records, "1.json"));
        var policy = Path.Combine(root, "recommendations.json");
        await File.WriteAllTextAsync(policy, """
            {
              "schemaVersion": "1.0",
              "recommendations": [{
                "role": "test-specialist",
                "disposition": "INSUFFICIENT_EVIDENCE",
                "scenario": "structured-help",
                "arms": ["optimized-new"],
                "rationale": "One reviewed fixture is not enough to justify a persistent role."
              }],
              "routing": [{
                "capability": "tests",
                "route": "AGENT",
                "scenario": "structured-help",
                "arms": ["optimized-new"],
                "rationale": "Behavioral adequacy requires agent reasoning after exact test facts."
              }]
            }
            """);

        try
        {
            var json = await AgentCapabilityAggregator.AggregateAsync(
                root, policy, "0123456789abcdef0123456789abcdef01234567",
                DateTimeOffset.Parse("2026-09-23T00:00:00Z"));
            var document = JsonNode.Parse(json)!;

            Assert.Equal("simplexidev/sdeveng", document["subject"]!["repository"]!.GetValue<string>());
            Assert.Equal("reviewed", document["provenance"]!["approval"]!.GetValue<string>());
            Assert.Contains(document["metrics"]!.AsArray(), metric =>
                metric!["name"]!.GetValue<string>() == "agent-test-specialist-insufficient-evidence");
            Assert.Contains(document["metrics"]!.AsArray(), metric =>
                metric!["name"]!.GetValue<string>() == "routing-tests-agent");
            Assert.Contains(document["metrics"]!.AsArray(), metric =>
                metric!["name"]!.GetValue<string>() == "agent-test-specialist-delegation-measurement-coverage");
            Assert.DoesNotContain("response", json, StringComparison.OrdinalIgnoreCase);

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

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
