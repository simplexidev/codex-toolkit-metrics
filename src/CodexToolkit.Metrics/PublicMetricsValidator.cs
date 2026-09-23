using System.Globalization;
using System.Text.Json;

namespace CodexToolkit.Metrics;

public static class PublicMetricsValidator
{
    private const long MaximumPublicFileBytes = 1_000_000;

    private static readonly HashSet<string> SensitivePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "apiKey", "email", "localPath", "privateSource", "prompt", "raw", "request",
        "response", "secret", "sourceText", "token", "transcript", "user"
    };

    private static readonly HashSet<string> Directions = new(StringComparer.Ordinal)
    {
        "higher-is-better", "lower-is-better", "neutral"
    };

    private static readonly HashSet<string> MeasurementKinds = new(StringComparer.Ordinal)
    {
        "measured", "derived", "estimated"
    };

    public static async Task<ValidationResult> ValidateFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return new ValidationResult(["Metrics file does not exist."]);
        }

        if (file.Length > MaximumPublicFileBytes)
        {
            return new ValidationResult([$"Metrics file exceeds {MaximumPublicFileBytes} bytes."]);
        }

        try
        {
            await using var stream = file.OpenRead();
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            ValidateRoot(document.RootElement, errors);
            FindSensitiveProperties(document.RootElement, "$", errors);
        }
        catch (JsonException exception)
        {
            errors.Add($"Invalid JSON: {exception.Message}");
        }

        return new ValidationResult(errors);
    }

    private static void ValidateRoot(JsonElement root, List<string> errors)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add("The document root must be an object.");
            return;
        }

        RequireString(root, "schemaVersion", errors, value => value == "1.0", "must equal '1.0'");
        RequireString(root, "generatedAt", errors,
            value => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
            "must be an ISO 8601 timestamp");

        if (RequireObject(root, "subject", errors) is { } subject)
        {
            RequireString(subject, "repository", errors,
                value => value == "simplexidev/codex-toolkit", "must identify simplexidev/codex-toolkit");
            RequireString(subject, "revision", errors,
                value => value.Length is >= 7 and <= 64 && value.All(Uri.IsHexDigit),
                "must be a 7-64 character hexadecimal revision");
        }

        if (RequireObject(root, "scenarios", errors) is { } scenarios)
        {
            var total = RequireNonNegativeInteger(scenarios, "total", errors);
            var passed = RequireNonNegativeInteger(scenarios, "passed", errors);
            if (total is not null && passed > total)
            {
                errors.Add("$.scenarios.passed must not exceed total.");
            }
        }

        ValidateMetrics(root, errors);

        if (RequireObject(root, "provenance", errors) is { } provenance)
        {
            RequirePositiveInteger(provenance, "sourceRuns", errors);
            if (!provenance.TryGetProperty("sanitized", out var sanitized) ||
                sanitized.ValueKind != JsonValueKind.True)
            {
                errors.Add("$.provenance.sanitized must be true.");
            }

            RequireString(provenance, "approval", errors,
                value => value is "synthetic" or "reviewed", "must equal 'synthetic' or 'reviewed'");
        }
    }

    private static void ValidateMetrics(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("metrics", out var metrics) || metrics.ValueKind != JsonValueKind.Array)
        {
            errors.Add("$.metrics must be an array.");
            return;
        }

        if (metrics.GetArrayLength() == 0)
        {
            errors.Add("$.metrics must contain at least one aggregate metric.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var metric in metrics.EnumerateArray())
        {
            var prefix = $"$.metrics[{index}]";
            if (metric.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{prefix} must be an object.");
                index++;
                continue;
            }

            var name = RequireString(metric, "name", errors,
                value => value.Length is > 0 and <= 80, "must contain 1-80 characters", prefix);
            if (name is not null && !names.Add(name))
            {
                errors.Add($"{prefix}.name must be unique.");
            }

            if (!metric.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Number ||
                !value.TryGetDouble(out var number) || !double.IsFinite(number))
            {
                errors.Add($"{prefix}.value must be a finite number.");
            }

            RequireString(metric, "unit", errors,
                unit => unit.Length is > 0 and <= 32, "must contain 1-32 characters", prefix);
            RequireString(metric, "direction", errors,
                direction => Directions.Contains(direction), "has an unsupported value", prefix);
            OptionalString(metric, "kind", errors,
                kind => MeasurementKinds.Contains(kind), "has an unsupported value", prefix);
            OptionalString(metric, "method", errors,
                method => method.Length is > 0 and <= 200, "must contain 1-200 characters", prefix);
            index++;
        }
    }

    private static void OptionalString(
        JsonElement parent,
        string name,
        List<string> errors,
        Func<string, bool> predicate,
        string predicateMessage,
        string prefix)
    {
        if (!parent.TryGetProperty(name, out var value)) return;
        if (value.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{prefix}.{name} must be a string.");
            return;
        }
        if (!predicate(value.GetString()!)) errors.Add($"{prefix}.{name} {predicateMessage}.");
    }

    private static JsonElement? RequireObject(JsonElement parent, string name, List<string> errors)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"$.{name} must be an object.");
            return null;
        }

        return value;
    }

    private static string? RequireString(
        JsonElement parent,
        string name,
        List<string> errors,
        Func<string, bool> predicate,
        string predicateMessage,
        string prefix = "$")
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{prefix}.{name} must be a string.");
            return null;
        }

        var text = value.GetString()!;
        if (!predicate(text))
        {
            errors.Add($"{prefix}.{name} {predicateMessage}.");
        }

        return text;
    }

    private static int? RequireNonNegativeInteger(JsonElement parent, string name, List<string> errors)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var number) || number < 0)
        {
            errors.Add($"$.scenarios.{name} must be a non-negative integer.");
            return null;
        }

        return number;
    }

    private static void RequirePositiveInteger(JsonElement parent, string name, List<string> errors)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var number) || number < 1)
        {
            errors.Add($"$.provenance.{name} must be a positive integer.");
        }
    }

    private static void FindSensitiveProperties(JsonElement element, string path, List<string> errors)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var propertyPath = $"{path}.{property.Name}";
                if (SensitivePropertyNames.Contains(property.Name))
                {
                    errors.Add($"{propertyPath} is forbidden in public metrics.");
                }

                FindSensitiveProperties(property.Value, propertyPath, errors);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                FindSensitiveProperties(item, $"{path}[{index}]", errors);
                index++;
            }
        }
    }
}
