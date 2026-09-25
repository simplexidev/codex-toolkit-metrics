namespace SdevEng.Metrics.Tests;

public sealed class StaticCostAnalyzerTests
{
    [Fact]
    public void SeparatesRoutingActivationLazyAndAgentCosts()
    {
        using var fixture = new StaticFixture();

        var report = StaticCostAnalyzer.Analyze(fixture.Root, "abcdef1");

        Assert.Equal("simplexidev/sdeveng", report.Subject);
        Assert.Equal(2, report.Summary.SkillCount);
        Assert.Equal(1, report.Summary.AgentCount);
        Assert.Equal(1, report.Summary.RoutingPairs);
        Assert.Equal(1, report.Summary.OverlappingRoutingPairs);
        var alpha = Assert.Single(report.Skills, item => item.Name == "alpha");
        Assert.Equal(1, alpha.LazyReferenceFiles);
        Assert.True(alpha.MaximumPossibleLoad.Bytes > alpha.SkillFile.Bytes);
        Assert.True(alpha.ActivationVisible.Tokens > alpha.AlwaysVisible.Tokens);
        var agent = Assert.Single(report.Agents);
        Assert.True(agent.Instructions.Bytes > 0);
        Assert.True(agent.Configuration.Bytes > 0);
        Assert.Equal("estimated", report.Summary.AlwaysVisible.TokenKind);
    }

    [Fact]
    public void PublicAggregateContainsNoSourceTextAndLabelsEstimates()
    {
        using var fixture = new StaticFixture();
        var report = StaticCostAnalyzer.Analyze(fixture.Root, "abcdef1");

        var json = StaticCostAnalyzer.PublicAggregate(report, DateTimeOffset.Parse("2026-09-22T00:00:00Z"));

        Assert.Contains("\"kind\": \"estimated\"", json, StringComparison.Ordinal);
        Assert.Contains("\"method\": \"OpenAI GPT-family", json, StringComparison.Ordinal);
        Assert.DoesNotContain("special private wording", json, StringComparison.Ordinal);
    }

    private sealed class StaticFixture : IDisposable
    {
        public StaticFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"static-cost-{Guid.NewGuid():N}");
            Skill("alpha", "Review .NET package changes.", "special private wording", "reference one");
            Skill("beta", "Audit .NET package safety.", "body two", null);
            var agents = Path.Combine(Root, "agents");
            Directory.CreateDirectory(agents);
            File.WriteAllText(Path.Combine(agents, "reviewer.toml"), """"
                name = "reviewer"
                model = "gpt-5"
                developer_instructions = """
                Review the actual diff.
                """
                """");
        }

        public string Root { get; }

        private void Skill(string name, string description, string body, string? reference)
        {
            var directory = Path.Combine(Root, "plugins", "sdeveng", "skills", name);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "SKILL.md"), $"---\nname: {name}\ndescription: {description}\n---\n\n# {name}\n\n{body}\n");
            if (reference is null) return;
            Directory.CreateDirectory(Path.Combine(directory, "references"));
            File.WriteAllText(Path.Combine(directory, "references", "one.md"), reference);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
