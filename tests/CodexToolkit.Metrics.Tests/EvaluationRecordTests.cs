using System.Text.Json;

namespace CodexToolkit.Metrics.Tests;

public sealed class EvaluationRecordTests
{
    [Fact]
    public async Task AcceptsCompleteVersionedFixture()
    {
        var path = Fixture("evaluation-valid-v1.json");

        var result = await EvaluationRecordValidator.ValidateFileAsync(path);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void RoundTripsArmNamesAndMeasurementKinds()
    {
        var json = File.ReadAllText(Fixture("evaluation-valid-v1.json"));
        var record = EvaluationRecordJson.Deserialize(json);

        Assert.NotNull(record);
        Assert.Equal(ComparisonArm.Optimized, record.Arm.Comparison);
        Assert.Equal(CustomizationArm.OptimizedOrNew, record.Arm.Customization);
        Assert.Equal(MeasurementKind.Unavailable, record.Quality.NestedDelegation.Correctness.Kind);

        var roundTrip = EvaluationRecordJson.Serialize(record);
        Assert.Contains("\"OPTIMIZED-or-NEW\"", roundTrip, StringComparison.Ordinal);
        Assert.Contains("\"unavailable\"", roundTrip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsValueForUnavailableMetric()
    {
        var json = File.ReadAllText(Fixture("evaluation-valid-v1.json"))
            .Replace(
                "\"value\": null, \"unit\": \"ratio\", \"kind\": \"unavailable\"",
                "\"value\": 1, \"unit\": \"ratio\", \"kind\": \"unavailable\"",
                StringComparison.Ordinal);
        var path = await WriteTemporaryFixture(json);

        try
        {
            var result = await EvaluationRecordValidator.ValidateFileAsync(path);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains(
                "value must be null when kind is 'unavailable'", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsUnknownFields()
    {
        var json = File.ReadAllText(Fixture("evaluation-valid-v1.json"))
            .Replace(
                "\"schemaVersion\": \"1.0.0\"",
                "\"schemaVersion\": \"1.0.0\", \"unexpected\": true",
                StringComparison.Ordinal);
        var path = await WriteTemporaryFixture(json);

        try
        {
            var result = await EvaluationRecordValidator.ValidateFileAsync(path);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("unexpected", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsNumericEnumRepresentations()
    {
        var json = File.ReadAllText(Fixture("evaluation-valid-v1.json"))
            .Replace("\"comparison\": \"OPTIMIZED\"", "\"comparison\": 2", StringComparison.Ordinal);
        var path = await WriteTemporaryFixture(json);

        try
        {
            var result = await EvaluationRecordValidator.ValidateFileAsync(path);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("comparison", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SchemaDefinesProvenanceForEveryNumericMetric()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Schema("evaluation-record-v1.schema.json")));
        var definitions = document.RootElement.GetProperty("$defs");
        var numericMetric = definitions.GetProperty("numericMetric");

        var required = numericMetric.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("value", required);
        Assert.Contains("kind", required);
        Assert.Contains("method", required);
        Assert.Equal(
            ["measured", "derived", "estimated", "unavailable"],
            numericMetric.GetProperty("properties").GetProperty("kind").GetProperty("enum")
                .EnumerateArray().Select(item => item.GetString()));
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static string Schema(string name) => Path.Combine(AppContext.BaseDirectory, "Schemas", name);

    private static async Task<string> WriteTemporaryFixture(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"codex-toolkit-evaluation-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, json);
        return path;
    }
}
