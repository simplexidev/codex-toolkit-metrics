using System.Text.Json;

namespace CodexToolkit.Metrics;

public static class DeterministicAssertions
{
    public static async Task<IReadOnlyList<AssertionResult>> EvaluateAsync(
        ScenarioExpectations expected,
        ExecutionResult execution,
        string workspace,
        CancellationToken cancellationToken = default)
    {
        var results = new List<AssertionResult>();
        if (expected.ExitCode is { } exitCode)
        {
            results.Add(new("exitCode", execution.ExitCode == exitCode,
                $"expected {exitCode}; observed {execution.ExitCode?.ToString() ?? "none"}"));
        }

        if (expected.OutputKind == OutputKind.Json)
        {
            var valid = true;
            try { using var _ = JsonDocument.Parse(execution.Response); }
            catch (JsonException) { valid = false; }
            results.Add(new("outputKind", valid, valid ? "valid JSON" : "response was not valid JSON"));
        }
        else if (expected.OutputKind == OutputKind.Text)
        {
            results.Add(new("outputKind", !string.IsNullOrWhiteSpace(execution.Response), "non-empty text response"));
        }

        foreach (var fragment in expected.ResponseContains)
        {
            results.Add(new($"responseContains:{fragment}",
                execution.Response.Contains(fragment, StringComparison.Ordinal), "literal ordinal match"));
        }

        foreach (var relativePath in expected.FilesExist)
        {
            var path = ResolveWorkspacePath(workspace, relativePath);
            results.Add(new($"fileExists:{relativePath}", File.Exists(path), "regular file"));
        }

        foreach (var expectation in expected.FileContains)
        {
            var path = ResolveWorkspacePath(workspace, expectation.Path);
            var contains = File.Exists(path) &&
                (await File.ReadAllTextAsync(path, cancellationToken)).Contains(expectation.Contains, StringComparison.Ordinal);
            results.Add(new($"fileContains:{expectation.Path}", contains, "literal ordinal match"));
        }

        AddObservationAssertions(results, "skill", expected.ActivatedSkills, execution.Observations.ActivatedSkills);
        AddObservationAssertions(results, "agent", expected.DelegatedAgents, execution.Observations.DelegatedAgents);
        return results;
    }

    private static void AddObservationAssertions(
        List<AssertionResult> results,
        string kind,
        IReadOnlyList<string> expected,
        IReadOnlyList<string> observed)
    {
        foreach (var name in expected)
        {
            results.Add(new($"{kind}Observed:{name}", observed.Contains(name, StringComparer.Ordinal),
                $"observed: {string.Join(", ", observed)}"));
        }
    }

    private static string ResolveWorkspacePath(string workspace, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Assertion paths must be workspace-relative.");
        }

        var root = Path.GetFullPath(workspace) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(relativePath, workspace);
        if (!candidate.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Assertion paths may not escape the isolated workspace.");
        }

        return candidate;
    }
}
