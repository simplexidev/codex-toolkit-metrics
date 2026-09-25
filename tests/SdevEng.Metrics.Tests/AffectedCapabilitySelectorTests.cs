namespace SdevEng.Metrics.Tests;

public sealed class AffectedCapabilitySelectorTests
{
    [Fact]
    public void SelectsOnlyDeclaredCapabilitiesForChangedPaths()
    {
        var plan = Plan(
            new EvaluationScenario { Id = "tests", Capability = "tests", Prompt = "x", AffectedPaths = ["src/**", "tests/**"], Expected = new ScenarioExpectations() },
            new EvaluationScenario { Id = "release", Capability = "release", Prompt = "x", AffectedPaths = [".github/**"], Expected = new ScenarioExpectations() });
        var selection = AffectedCapabilitySelector.Select(plan, ["src/Parser/Parser.cs"]);
        Assert.Equal("affected", selection.Mode);
        Assert.Equal(["tests"], selection.ScenarioIds);
    }

    [Fact]
    public void RequiresFullSuiteWhenDiffIsUnavailable()
    {
        var selection = AffectedCapabilitySelector.Select(Plan(Scenario("one"), Scenario("two")), []);
        Assert.Equal("full", selection.Mode);
        Assert.Equal(["one", "two"], selection.ScenarioIds);
    }

    private static EvaluationScenario Scenario(string id) => new() { Id = id, Capability = id, Prompt = "x", Expected = new ScenarioExpectations() };
    private static EvaluationPlan Plan(params EvaluationScenario[] scenarios) => new()
    {
        SchemaVersion = "1.2",
        Suite = "test",
        FixtureRoot = "fixture",
        Repetitions = 1,
        TimeoutSeconds = 1,
        Executor = new RunnerProvider { Provider = "openai", Model = "gpt-5", Reasoning = "low", Version = "test" },
        Judge = new JudgeConfiguration { Path = JudgingPath.Deterministic },
        Provenance = new RunProvenance { Toolkit = new RevisionIdentity { Sha = "0123456", Version = "test" }, Metrics = new RevisionIdentity { Sha = "0123456", Version = "test" } },
        Arms = [new EvaluationArmDefinition { Id = "arm", Comparison = ComparisonArm.Vanilla, Customization = CustomizationArm.BuiltinOrNoCustom }],
        Scenarios = scenarios
    };
}
