using System.Text.Json;

namespace CodexToolkit.Metrics;

public static class CodexJsonlParser
{
    public static (string Response, ExecutionUsage Usage, ExecutionObservations Observations) Parse(string jsonl)
    {
        string response = "";
        long? input = null;
        long? output = null;
        long? total = null;
        var turns = 0;
        var toolCalls = 0;
        var skills = new HashSet<string>(StringComparer.Ordinal);
        var agents = new HashSet<string>(StringComparer.Ordinal);
        var tools = new HashSet<string>(StringComparer.Ordinal);
        int? maximumDelegationDepth = null;
        bool? contextIsolated = null;

        foreach (var line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            JsonDocument document;
            try { document = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            using (document)
            {
                var root = document.RootElement;
                var type = Text(root, "type") ?? "";
                if (type == "turn.completed") turns++;
                if (root.TryGetProperty("usage", out var usage))
                {
                    input = Maximum(input, Number(usage, "input_tokens"));
                    output = Maximum(output, Number(usage, "output_tokens"));
                    total = Maximum(total, Number(usage, "total_tokens"));
                }

                if (root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
                {
                    var itemType = Text(item, "type") ?? "";
                    if (type == "item.completed" && itemType == "agent_message")
                    {
                        response = Text(item, "text") ?? response;
                    }

                    if (type == "item.started" && itemType is "command_execution" or "mcp_tool_call" or "web_search")
                    {
                        toolCalls++;
                        CaptureTool(item, itemType, tools);
                    }

                    CaptureObservation(item, itemType, skills, agents);
                    maximumDelegationDepth = Maximum(maximumDelegationDepth, Integer(item, "delegation_depth"));
                    contextIsolated ??= Boolean(item, "context_isolated");
                }

                CaptureNamed(root, "activated_skill", skills);
                CaptureNamed(root, "delegated_agent", agents);
                CaptureNamed(root, "invoked_tool", tools);
                maximumDelegationDepth = Maximum(maximumDelegationDepth, Integer(root, "delegation_depth"));
                contextIsolated ??= Boolean(root, "context_isolated");
            }
        }

        total ??= input.HasValue || output.HasValue ? (input ?? 0) + (output ?? 0) : null;
        return (response, new ExecutionUsage
        {
            InputTokens = input,
            OutputTokens = output,
            TotalTokens = total,
            Turns = turns == 0 ? null : turns,
            ToolCalls = toolCalls
        }, new ExecutionObservations
        {
            ActivatedSkills = skills.Order(StringComparer.Ordinal).ToArray(),
            DelegatedAgents = agents.Order(StringComparer.Ordinal).ToArray(),
            InvokedTools = tools.Order(StringComparer.Ordinal).ToArray(),
            MaximumDelegationDepth = maximumDelegationDepth,
            ContextIsolated = contextIsolated
        });
    }

    private static void CaptureTool(JsonElement item, string itemType, HashSet<string> tools)
    {
        var name = Text(item, "name") ?? Text(item, "tool") ?? Text(item, "command");
        tools.Add(string.IsNullOrWhiteSpace(name) ? itemType : name);
    }

    private static void CaptureObservation(
        JsonElement item,
        string itemType,
        HashSet<string> skills,
        HashSet<string> agents)
    {
        if (itemType.Contains("skill", StringComparison.OrdinalIgnoreCase))
        {
            CaptureNamed(item, "name", skills);
            CaptureNamed(item, "skill", skills);
        }

        if (itemType.Contains("agent", StringComparison.OrdinalIgnoreCase) && itemType != "agent_message")
        {
            CaptureNamed(item, "name", agents);
            CaptureNamed(item, "agent", agents);
        }

        CaptureNamed(item, "activated_skill", skills);
        CaptureNamed(item, "delegated_agent", agents);
    }

    private static void CaptureNamed(JsonElement element, string property, HashSet<string> destination)
    {
        var value = Text(element, property);
        if (!string.IsNullOrWhiteSpace(value)) destination.Add(value);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var number) ? number : null;

    private static int? Integer(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static bool? Boolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static long? Maximum(long? left, long? right) =>
        right.HasValue && (!left.HasValue || right > left) ? right : left;

    private static int? Maximum(int? left, int? right) =>
        right.HasValue && (!left.HasValue || right > left) ? right : left;
}
