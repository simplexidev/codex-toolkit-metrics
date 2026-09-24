namespace CodexToolkit.Metrics.Tests;

public sealed class ToolkitMetadataValidatorTests
{
    [Fact]
    public async Task ValidatesPlanRecommendationsAndCapabilitiesAgainstToolkitMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"toolkit-metadata-{Guid.NewGuid():N}");
        var toolkit = Path.Combine(root, "toolkit");
        Directory.CreateDirectory(Path.Combine(toolkit, "config"));
        await File.WriteAllTextAsync(Path.Combine(toolkit, "config", "agent-candidates.json"), """
            {"builtInRoles":[{"id":"root-coordinator"}],"toolkitRoles":[{"id":"reviewer"}],"candidateRoles":[],"scenarios":[{"id":"review-scenario"}]}
            """);
        await File.WriteAllTextAsync(Path.Combine(toolkit, "config", "capabilities.json"), """
            {"capabilities":[{"id":"security-sarif"}]}
            """);
        var plan = RunnerTestSupport.Plan("fixture") with
        {
            Scenarios = [RunnerTestSupport.Scenario() with { Id = "review-scenario" }]
        };
        var planPath = Path.Combine(root, "plan.json");
        await File.WriteAllTextAsync(planPath,
            System.Text.Json.JsonSerializer.Serialize(plan, EvaluationRecordJson.Options));
        var recommendations = Path.Combine(root, "recommendations.json");
        await File.WriteAllTextAsync(recommendations, """
            {"recommendations":[{"role":"reviewer","scenario":"review-scenario"}],"routing":[{"capability":"security-sarif"}]}
            """);

        try
        {
            var result = await ToolkitMetadataValidator.ValidateAsync(toolkit, planPath, recommendations);
            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
