using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexToolkit.Metrics;

public sealed record EvaluationPlan
{
    public const string CurrentSchemaVersion = "1.2";

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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JevJudgeConfiguration? Jev { get; init; }
}

public sealed record JevJudgeConfiguration
{
    public string ApiUrl { get; init; } = "https://api.typesafe.ai/v1/systemone";
    public string Model { get; init; } = "jev-latest";
    public int TimeoutSeconds { get; init; } = 15;
    public int MaxInputBytes { get; init; } = 16_384;
    public decimal MinConfidence { get; init; } = 0.8m;
    public decimal PassScore { get; init; } = 0.6m;
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
    public string? Role { get; init; }
    public required ComparisonArm Comparison { get; init; }
    public required CustomizationArm Customization { get; init; }
    public IReadOnlyList<string> SkillPaths { get; init; } = [];
    public IReadOnlyList<string> AgentPaths { get; init; } = [];
    public string? InstructionFile { get; init; }
    public string Sandbox { get; init; } = "read-only";
    public bool ContextIsolated { get; init; } = true;
}

public sealed record EvaluationScenario
{
    public required string Id { get; init; }
    public required string Capability { get; init; }
    public string? Prompt { get; init; }
    public string? PromptFile { get; init; }
    public IReadOnlyList<string> ArmIds { get; init; } = [];
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
    public IReadOnlyList<string> InvokedTools { get; init; } = [];
    public RoutingCase ActivationCase { get; init; } = RoutingCase.Unspecified;
    public RoutingCase DelegationCase { get; init; } = RoutingCase.Unspecified;
    public bool? NestedDelegation { get; init; }
    public bool? ContextIsolation { get; init; }
    public string? SemanticRubric { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<RoutingCase>))]
public enum RoutingCase
{
    [JsonStringEnumMemberName("unspecified")]
    Unspecified,
    [JsonStringEnumMemberName("should-activate")]
    ShouldActivate,
    [JsonStringEnumMemberName("should-not-activate")]
    ShouldNotActivate,
    [JsonStringEnumMemberName("ambiguous")]
    Ambiguous
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
        if (plan.SchemaVersion is not ("1.0" or "1.1" or EvaluationPlan.CurrentSchemaVersion))
        {
            errors.Add($"schemaVersion must equal '1.0', '1.1', or '{EvaluationPlan.CurrentSchemaVersion}'.");
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
        else if (plan.Judge.Path == JudgingPath.Jev)
        {
            ValidateOpenAiProvider(plan.Judge.Provider, plan.Judge.Model, "judge fallback", errors);
            ValidateJev(plan.Judge.Jev, errors);
        }
        else if (plan.Judge.Provider is not null || plan.Judge.Model is not null)
        {
            errors.Add("judge provider/model are only valid for gpt or jev judging paths.");
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
            if (arm.Role is not null) Require(arm.Role, $"arm '{arm.Id}' role", errors);
            foreach (var path in arm.SkillPaths.Concat(arm.AgentPaths))
            {
                Require(path, $"arm '{arm.Id}' overlay path", errors);
            }
            if (arm.InstructionFile is not null) Require(arm.InstructionFile, $"arm '{arm.Id}' instructionFile", errors);
            if (arm.Sandbox is not ("read-only" or "workspace-write"))
                errors.Add($"arm '{arm.Id}' sandbox must equal 'read-only' or 'workspace-write'.");
        }

        foreach (var scenario in plan.Scenarios ?? [])
        {
            Require(scenario.Capability, $"scenario '{scenario.Id}' capability", errors);
            if (string.IsNullOrWhiteSpace(scenario.Prompt) == string.IsNullOrWhiteSpace(scenario.PromptFile))
            {
                errors.Add($"scenario '{scenario.Id}' must specify exactly one of prompt or promptFile.");
            }
            if (scenario.Expected is null) errors.Add($"scenario '{scenario.Id}' expected is required.");
            else
            {
                ValidateRoutingCase(scenario.Expected.ActivationCase, scenario.Expected.ActivatedSkills,
                    $"scenario '{scenario.Id}' activation", errors);
                ValidateRoutingCase(scenario.Expected.DelegationCase, scenario.Expected.DelegatedAgents,
                    $"scenario '{scenario.Id}' delegation", errors);
            }
            foreach (var armId in scenario.ArmIds)
            {
                if (!(plan.Arms ?? []).Any(arm => string.Equals(arm.Id, armId, StringComparison.Ordinal)))
                    errors.Add($"scenario '{scenario.Id}' references unknown arm '{armId}'.");
            }
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

    private static void ValidateJev(JevJudgeConfiguration? jev, List<string> errors)
    {
        if (jev is null)
        {
            errors.Add("judge.jev is required for the jev judging path.");
            return;
        }
        if (!Uri.TryCreate(jev.ApiUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            errors.Add("judge.jev.apiUrl must use HTTPS without credentials, query, or fragment.");
        Require(jev.Model, "judge.jev.model", errors);
        if (jev.TimeoutSeconds is < 1 or > 120) errors.Add("judge.jev.timeoutSeconds must be between 1 and 120.");
        if (jev.MaxInputBytes is < 256 or > 65_536) errors.Add("judge.jev.maxInputBytes must be between 256 and 65536.");
        if (jev.MinConfidence is < 0 or > 1) errors.Add("judge.jev.minConfidence must be in [0,1].");
        if (jev.PassScore is < 0 or > 1) errors.Add("judge.jev.passScore must be in [0,1].");
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

    private static void ValidateRoutingCase(
        RoutingCase routingCase,
        IReadOnlyList<string> expected,
        string path,
        List<string> errors)
    {
        if (routingCase == RoutingCase.ShouldActivate && expected.Count == 0)
            errors.Add($"{path} should-activate requires at least one expected name.");
        if (routingCase == RoutingCase.ShouldNotActivate && expected.Count != 0)
            errors.Add($"{path} should-not-activate cannot include expected names.");
    }

    private static void Require(string? value, string path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) errors.Add($"{path} must be a non-empty string.");
    }
}
