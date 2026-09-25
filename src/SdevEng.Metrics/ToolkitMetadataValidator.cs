using System.Text.Json;

namespace SdevEng.Metrics;

public static class ToolkitMetadataValidator
{
    public static async Task<ValidationResult> ValidateAsync(
        string toolkitDirectory,
        string planPath,
        string recommendationPath,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        try
        {
            var candidatePath = Path.Combine(toolkitDirectory, "config", "agent-candidates.json");
            var capabilityPath = Path.Combine(toolkitDirectory, "config", "capabilities.json");
            using var candidates = JsonDocument.Parse(await File.ReadAllTextAsync(candidatePath, cancellationToken));
            using var capabilities = JsonDocument.Parse(await File.ReadAllTextAsync(capabilityPath, cancellationToken));
            using var recommendations = JsonDocument.Parse(await File.ReadAllTextAsync(recommendationPath, cancellationToken));
            var loaded = await EvaluationPlanLoader.LoadAsync(planPath, cancellationToken);
            errors.AddRange(loaded.Errors);
            if (loaded.Plan is null) return new ValidationResult(errors);

            var manifestScenarios = Values(candidates.RootElement.GetProperty("scenarios"), "id");
            var planScenarios = loaded.Plan.Scenarios.Select(scenario => scenario.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var missing in manifestScenarios.Except(planScenarios).Order())
                errors.Add($"Evaluation plan is missing toolkit candidate scenario '{missing}'.");

            var roles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in new[] { "builtInRoles", "toolkitRoles", "candidateRoles" })
                roles.UnionWith(Values(candidates.RootElement.GetProperty(property), "id"));
            var recommendationItems = recommendations.RootElement.GetProperty("recommendations");
            foreach (var item in recommendationItems.EnumerateArray())
            {
                var role = item.GetProperty("role").GetString()!;
                var scenario = item.GetProperty("scenario").GetString()!;
                if (!roles.Contains(role)) errors.Add($"Recommendation references unknown toolkit role '{role}'.");
                if (!manifestScenarios.Contains(scenario))
                    errors.Add($"Recommendation for '{role}' references unknown candidate scenario '{scenario}'.");
            }

            var capabilityIds = Values(capabilities.RootElement.GetProperty("capabilities"), "id");
            foreach (var item in recommendations.RootElement.GetProperty("routing").EnumerateArray())
            {
                var capability = item.GetProperty("capability").GetString()!;
                if (!capabilityIds.Contains(capability))
                    errors.Add($"Routing recommendation references unknown toolkit capability '{capability}'.");
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException)
        {
            errors.Add($"Unable to validate toolkit evaluation metadata: {exception.Message}");
        }
        return new ValidationResult(errors);
    }

    private static HashSet<string> Values(JsonElement array, string property) => array.EnumerateArray()
        .Select(item => item.GetProperty(property).GetString()!)
        .ToHashSet(StringComparer.Ordinal);
}
