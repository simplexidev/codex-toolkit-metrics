using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexToolkit.Metrics;

public sealed record EvaluationPlan
{
    public const string CurrentSchemaVersion = "1.0";

    public required string SchemaVersion { get; init; }
    public required string Suite { get; init; }
    public required string FixtureRoot { get; init; }
    public required int Repetitions { get; init; }
    public required int TimeoutSeconds { get; init; }
    public required RunnerProvider Executor { get; init; }
    public required JudgeConfiguration Judge { get; init; }
    public required RunProvenance Provenance { get; init; }
    public required IReadOnlyList<EvaluationArmDefinition> Arms { get; init; }
    public required IReadOnlyList<EvaluationScenario> Scenarios { get; init; }
}

public sealed record RunnerProvider
{
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public required string Reasoning { get; init; }
    public required string Version { get; init; }
    public string Executable { get; init; } = "codex";
}

public sealed record JudgeConfiguration
{
    public required JudgingPath Path { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
}

public sealed record RunProvenance
{
    public required RevisionIdentity Toolkit { get; init; }
    public required RevisionIdentity Metrics { get; init; }
    public UpstreamIdentity Upstream { get; init; } = new();
}

public sealed record EvaluationArmDefinition
{
    public required string Id { get; init; }
    public required ComparisonArm Comparison { get; init; }
    public required CustomizationArm Customization { get; init; }
    public IReadOnlyList<string> SkillPaths { get; init; } = [];
    public IReadOnlyList<string> AgentPaths { get; init; } = [];
}

public sealed record EvaluationScenario
{
    public required string Id { get; init; }
    public required string Capability { get; init; }
    public string? Prompt { get; init; }
    public string? PromptFile { get; init; }
    public required ScenarioExpectations Expected { get; init; }
}

public sealed record ScenarioExpectations
{
    public int? ExitCode { get; init; }
    public OutputKind OutputKind { get; init; } = OutputKind.Any;
    public IReadOnlyList<string> ResponseContains { get; init; } = [];
    public IReadOnlyList<string> FilesExist { get; init; } = [];
    public IReadOnlyList<FileContentExpectation> FileContains { get; init; } = [];
    public IReadOnlyList<string> ActivatedSkills { get; init; } = [];
    public IReadOnlyList<string> DelegatedAgents { get; init; } = [];
}

public sealed record FileContentExpectation
{
    public required string Path { get; init; }
    public required string Contains { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<JudgingPath>))]
public enum JudgingPath
{
    [JsonStringEnumMemberName("deterministic")]
    Deterministic,
    [JsonStringEnumMemberName("jev")]
    Jev,
    [JsonStringEnumMemberName("gpt")]
    Gpt
}

[JsonConverter(typeof(JsonStringEnumConverter<OutputKind>))]
public enum OutputKind
{
    [JsonStringEnumMemberName("any")]
    Any,
    [JsonStringEnumMemberName("text")]
    Text,
    [JsonStringEnumMemberName("json")]
    Json
}

public static class EvaluationPlanLoader
{
    public static async Task<(EvaluationPlan? Plan, IReadOnlyList<string> Errors)> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var plan = JsonSerializer.Deserialize<EvaluationPlan>(json, EvaluationRecordJson.Options);
            return plan is null
                ? (null, ["The evaluation plan must be an object."])
                : (plan, Validate(plan));
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return (null, [$"Unable to read evaluation plan: {exception.Message}"]);
        }
    }

    public static IReadOnlyList<string> Validate(EvaluationPlan plan)
    {
        var errors = new List<string>();
        if (plan.SchemaVersion != EvaluationPlan.CurrentSchemaVersion)
        {
            errors.Add($"schemaVersion must equal '{EvaluationPlan.CurrentSchemaVersion}'.");
        }

        Require(plan.Suite, "suite", errors);
        Require(plan.FixtureRoot, "fixtureRoot", errors);
        if (plan.Repetitions < 1) errors.Add("repetitions must be at least 1.");
        if (plan.TimeoutSeconds is < 1 or > 3600) errors.Add("timeoutSeconds must be between 1 and 3600.");

        ValidateOpenAiProvider(plan.Executor?.Provider, plan.Executor?.Model, "executor", errors);
        Require(plan.Executor?.Reasoning, "executor.reasoning", errors);
        Require(plan.Executor?.Version, "executor.version", errors);
        Require(plan.Executor?.Executable, "executor.executable", errors);
        if (plan.Judge is null)
        {
            errors.Add("judge is required.");
        }
        else if (plan.Judge.Path == JudgingPath.Gpt)
        {
            ValidateOpenAiProvider(plan.Judge.Provider, plan.Judge.Model, "judge", errors);
        }
        else if (plan.Judge.Provider is not null || plan.Judge.Model is not null)
        {
            errors.Add("judge provider/model are only valid for the gpt judging path.");
        }

        if (plan.Arms is null || plan.Arms.Count == 0) errors.Add("arms must contain at least one arm.");
        if (plan.Scenarios is null || plan.Scenarios.Count == 0) errors.Add("scenarios must contain at least one scenario.");
        if (plan.Provenance is null)
        {
            errors.Add("provenance is required.");
        }
        else if (plan.Provenance.Toolkit is null || plan.Provenance.Metrics is null)
        {
            errors.Add("provenance.toolkit and provenance.metrics are required.");
        }
        else
        {
            ValidateRevision(plan.Provenance.Toolkit, "provenance.toolkit", errors);
            ValidateRevision(plan.Provenance.Metrics, "provenance.metrics", errors);
        }
        ValidateUnique(plan.Arms?.Select(arm => arm.Id), "arm", errors);
        ValidateUnique(plan.Scenarios?.Select(scenario => scenario.Id), "scenario", errors);

        foreach (var arm in plan.Arms ?? [])
        {
            foreach (var path in arm.SkillPaths.Concat(arm.AgentPaths))
            {
                Require(path, $"arm '{arm.Id}' overlay path", errors);
            }
        }

        foreach (var scenario in plan.Scenarios ?? [])
        {
            Require(scenario.Capability, $"scenario '{scenario.Id}' capability", errors);
            if (string.IsNullOrWhiteSpace(scenario.Prompt) == string.IsNullOrWhiteSpace(scenario.PromptFile))
            {
                errors.Add($"scenario '{scenario.Id}' must specify exactly one of prompt or promptFile.");
            }
            if (scenario.Expected is null) errors.Add($"scenario '{scenario.Id}' expected is required.");
        }

        return errors;
    }

    private static void ValidateRevision(RevisionIdentity revision, string path, List<string> errors)
    {
        if (revision.Sha is null || revision.Sha.Length is < 7 or > 64 || !revision.Sha.All(Uri.IsHexDigit))
        {
            errors.Add($"{path}.sha must be a 7-64 character hexadecimal revision.");
        }
        Require(revision.Version, $"{path}.version", errors);
    }

    private static void ValidateOpenAiProvider(string? provider, string? model, string path, List<string> errors)
    {
        if (!string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{path}.provider must be 'openai'; non-OpenAI executors and judges are not supported.");
        }

        Require(model, $"{path}.model", errors);
        if (model?.Contains("claude", StringComparison.OrdinalIgnoreCase) == true)
        {
            errors.Add($"{path}.model must be an OpenAI/GPT model, not Claude.");
        }
    }

    private static void ValidateUnique(IEnumerable<string>? values, string name, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values ?? [])
        {
            Require(value, $"{name}.id", errors);
            if (!seen.Add(value)) errors.Add($"{name} id '{value}' must be unique.");
        }
    }

    private static void Require(string? value, string path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) errors.Add($"{path} must be a non-empty string.");
    }
}
