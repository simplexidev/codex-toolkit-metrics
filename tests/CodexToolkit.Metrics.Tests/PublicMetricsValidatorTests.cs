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

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
