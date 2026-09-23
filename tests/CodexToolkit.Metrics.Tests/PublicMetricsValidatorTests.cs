namespace CodexToolkit.Metrics.Tests;

public sealed class PublicMetricsValidatorTests
{
    [Fact]
    public async Task AcceptsSanitizedAggregateFixture()
    {
        var result = await PublicMetricsValidator.ValidateFileAsync(Fixture("public-valid.json"));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public async Task RejectsSensitiveRawFields()
    {
        var result = await PublicMetricsValidator.ValidateFileAsync(Fixture("public-sensitive.json"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("prompt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsPassedCountAboveTotal()
    {
        var result = await PublicMetricsValidator.ValidateFileAsync(Fixture("public-invalid-count.json"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("must not exceed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("localPath", "/home/operator/private.txt", "forbidden")]
    [InlineData("method", "/Users/operator/private.txt", "absolute path")]
    [InlineData("method", "TYPESAFE_API_KEY=not-a-real-value", "secret-like")]
    [InlineData("method", "raw prompt: private fixture text", "raw or private")]
    [InlineData("logs", "build output", "forbidden")]
    public async Task RejectsUnsafePublicationContent(string property, string value, string expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"metrics-public-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "unsafe.json");
        var json = $$"""
            {
              "schemaVersion": "1.0",
              "generatedAt": "2026-09-22T00:00:00Z",
              "subject": { "repository": "simplexidev/codex-toolkit", "revision": "0123456789abcdef0123456789abcdef01234567" },
              "scenarios": { "total": 1, "passed": 1 },
              "metrics": [{ "name": "fixture", "value": 1, "unit": "count", "direction": "neutral", "{{property}}": "{{value}}" }],
              "provenance": { "sourceRuns": 1, "sanitized": true, "approval": "synthetic" }
            }
            """;
        await File.WriteAllTextAsync(path, json);

        try
        {
            var result = await PublicMetricsValidator.ValidateFileAsync(path);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains(expected, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
