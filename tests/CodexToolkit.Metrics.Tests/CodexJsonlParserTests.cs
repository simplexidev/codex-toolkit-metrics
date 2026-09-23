namespace CodexToolkit.Metrics.Tests;

public sealed class CodexJsonlParserTests
{
    [Fact]
    public void ParsesResponseUsageToolsAndRoutingObservations()
    {
        var jsonl = """
            {"type":"item.started","item":{"type":"command_execution","command":"dotnet test"}}
            {"type":"skill.activated","activated_skill":"documents"}
            {"type":"item.completed","item":{"type":"subagent","delegated_agent":"reviewer","delegation_depth":2,"context_isolated":true}}
            {"type":"item.completed","item":{"type":"agent_message","text":"done"}}
            {"type":"turn.completed","usage":{"input_tokens":12,"output_tokens":3,"total_tokens":15}}
            """;

        var parsed = CodexJsonlParser.Parse(jsonl);

        Assert.Equal("done", parsed.Response);
        Assert.Equal(12, parsed.Usage.InputTokens);
        Assert.Equal(3, parsed.Usage.OutputTokens);
        Assert.Equal(15, parsed.Usage.TotalTokens);
        Assert.Equal(1, parsed.Usage.ToolCalls);
        Assert.Equal(1, parsed.Usage.Turns);
        Assert.Equal(["documents"], parsed.Observations.ActivatedSkills);
        Assert.Equal(["reviewer"], parsed.Observations.DelegatedAgents);
        Assert.Equal(["dotnet test"], parsed.Observations.InvokedTools);
        Assert.Equal(2, parsed.Observations.MaximumDelegationDepth);
        Assert.True(parsed.Observations.ContextIsolated);
    }

    [Fact]
    public void KeepsUnavailableUsageNullForPartialEvents()
    {
        var parsed = CodexJsonlParser.Parse("{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"ok\"}}");

        Assert.Null(parsed.Usage.InputTokens);
        Assert.Null(parsed.Usage.TotalTokens);
        Assert.Equal(0, parsed.Usage.ToolCalls);
    }
}
