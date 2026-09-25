using System.Text.Json;

namespace CodexToolkit.Metrics;

public static class PagesPublisher
{
    private const string ManifestName = "publication-manifest.json";
    private static readonly string[] DashboardFiles = ["index.html", "styles.css", "app.js"];

    public static async Task<ValidationResult> PublishAsync(
        string dashboardDirectory,
        string publicDataDirectory,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var errors = DashboardValidator.Validate(dashboardDirectory).ToList();
        var output = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(output) || File.Exists(output))
        {
            errors.Add($"Publication output already exists: {output}");
        }

        var manifestPath = Path.Combine(publicDataDirectory, ManifestName);
        var approved = await ReadManifestAsync(manifestPath, errors, cancellationToken);
        ValidateApprovalSet(publicDataDirectory, approved, errors);

        foreach (var file in DashboardFiles)
        {
            var path = Path.Combine(dashboardDirectory, file);
            if (!File.Exists(path)) continue;
            if (new FileInfo(path).LinkTarget is not null)
            {
                errors.Add($"Dashboard publication source must not be a symbolic link: {file}");
                continue;
            }
            var contents = await File.ReadAllTextAsync(path, cancellationToken);
            errors.AddRange(PublicationSafety.ValidateText(contents, $"dashboard/{file}"));
        }

        foreach (var artifact in approved)
        {
            var validation = await PublicMetricsValidator.ValidateFileAsync(
                Path.Combine(publicDataDirectory, artifact), cancellationToken);
            errors.AddRange(validation.Errors.Select(error => $"{artifact}: {error}"));
            await using var stream = File.OpenRead(Path.Combine(publicDataDirectory, artifact));
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("provenance", out var provenance) ||
                !provenance.TryGetProperty("approval", out var approval) || approval.GetString() != "reviewed")
                errors.Add($"{artifact}: production publication requires reviewed provenance; synthetic artifacts belong in test fixtures.");
        }

        if (errors.Count > 0) return new ValidationResult(errors);

        Directory.CreateDirectory(output);
        foreach (var file in DashboardFiles)
        {
            File.Copy(Path.Combine(dashboardDirectory, file), Path.Combine(output, file));
        }

        var dataOutput = Path.Combine(output, "data");
        Directory.CreateDirectory(dataOutput);
        File.Copy(manifestPath, Path.Combine(dataOutput, ManifestName));
        foreach (var artifact in approved)
        {
            var destination = Path.Combine(dataOutput, artifact.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(publicDataDirectory, artifact), destination);
        }

        await File.WriteAllTextAsync(Path.Combine(output, ".nojekyll"), string.Empty, cancellationToken);
        return new ValidationResult([]);
    }

    private static async Task<IReadOnlyList<string>> ReadManifestAsync(
        string path,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            errors.Add($"Missing publication manifest: {path}");
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var properties = root.ValueKind == JsonValueKind.Object
                ? root.EnumerateObject().Select(property => property.Name).ToArray()
                : [];
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schemaVersion", out var version) ||
                version.GetString() != "1.0" ||
                !root.TryGetProperty("artifacts", out var artifacts) ||
                artifacts.ValueKind != JsonValueKind.Array)
            {
                errors.Add("Publication manifest must have schemaVersion '1.0' and an artifacts array.");
                return [];
            }

            if (properties.Length != 2 || properties.Any(name => name is not "schemaVersion" and not "artifacts"))
            {
                errors.Add("Publication manifest contains unsupported properties.");
            }

            var approved = new List<string>();
            foreach (var value in artifacts.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    errors.Add("Every publication manifest artifact must be a string.");
                    continue;
                }

                var artifact = value.GetString()!.Replace('\\', '/');
                errors.AddRange(PublicationSafety.ValidateText(artifact, "publication manifest artifact"));
                if (string.IsNullOrWhiteSpace(artifact) || Path.IsPathRooted(artifact) ||
                    artifact.Split('/').Contains("..", StringComparer.Ordinal) ||
                    !artifact.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Unsafe or unsupported publication artifact: {artifact}");
                    continue;
                }

                approved.Add(artifact);
            }

            if (approved.Count == 0) errors.Add("Publication manifest must approve at least one artifact.");
            if (approved.Count != approved.Distinct(StringComparer.Ordinal).Count())
            {
                errors.Add("Publication manifest artifacts must be unique.");
            }

            return approved;
        }
        catch (JsonException exception)
        {
            errors.Add($"Invalid publication manifest JSON: {exception.Message}");
            return [];
        }
    }

    private static void ValidateApprovalSet(
        string publicDataDirectory,
        IReadOnlyList<string> approved,
        List<string> errors)
    {
        var actual = Directory.Exists(publicDataDirectory)
            ? Directory.EnumerateFiles(publicDataDirectory, "*.json", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(publicDataDirectory, path).Replace('\\', '/'))
                .Where(path => !string.Equals(path, ManifestName, StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal)
            : [];
        var expected = approved.ToHashSet(StringComparer.Ordinal);

        foreach (var missing in expected.Except(actual).Order())
        {
            errors.Add($"Approved publication artifact does not exist: {missing}");
        }


        foreach (var artifact in expected.Intersect(actual))
        {
            if (new FileInfo(Path.Combine(publicDataDirectory, artifact)).LinkTarget is not null)
            {
                errors.Add($"Publication artifact must not be a symbolic link: {artifact}");
            }
        }

        foreach (var unapproved in actual.Except(expected).Order())
        {
            errors.Add($"Unapproved public JSON artifact: {unapproved}");
        }
    }
}
