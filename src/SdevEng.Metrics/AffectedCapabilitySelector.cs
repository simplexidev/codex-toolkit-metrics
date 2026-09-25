using System.Text.Json;

namespace SdevEng.Metrics;

/// <summary>Deterministically narrows a suite from reviewed repository-relative changed paths.</summary>
public static class AffectedCapabilitySelector
{
    public static AffectedCapabilitySelection Select(EvaluationPlan plan, IEnumerable<string> changedPaths)
    {
        var paths = changedPaths.Select(Normalize).Where(path => path.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (paths.Length == 0)
            return new AffectedCapabilitySelection("full", plan.Scenarios.Select(x => x.Id).ToArray(), [],
                "No changed paths were supplied; the complete suite is required.");

        var selected = plan.Scenarios.Where(scenario => scenario.AffectedPaths.Count > 0 &&
            scenario.AffectedPaths.Any(pattern => paths.Any(path => Matches(pattern, path))))
            .Select(scenario => scenario.Id).ToArray();
        return new AffectedCapabilitySelection("affected", selected, paths,
            selected.Length == 0 ? "No declared capability is affected by the supplied paths." :
                "Scenarios were selected by declared affected-path globs.");
    }

    public static async Task<IReadOnlyList<string>> LoadChangedPathsAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var document = JsonSerializer.Deserialize<ChangedPathsDocument>(json, EvaluationRecordJson.Options)
            ?? throw new InvalidOperationException("Changed paths must be a JSON object.");
        if (document.SchemaVersion != "1.0" || document.Paths is null)
            throw new InvalidOperationException("Changed paths schemaVersion must equal '1.0' and paths must be present.");
        return document.Paths;
    }

    private static bool Matches(string pattern, string path)
    {
        pattern = Normalize(pattern);
        if (pattern.EndsWith("/**", StringComparison.Ordinal))
            return path.StartsWith(pattern[..^2], StringComparison.Ordinal);
        return string.Equals(pattern, path, StringComparison.Ordinal);
    }

    private static string Normalize(string value) => value.Trim().Replace('\\', '/').TrimStart('/');

    private sealed record ChangedPathsDocument(string SchemaVersion, IReadOnlyList<string> Paths);
}

public sealed record AffectedCapabilitySelection(
    string Mode, IReadOnlyList<string> ScenarioIds, IReadOnlyList<string> ChangedPaths, string Reason);
