using System.Text.Json;
using System.Text.Json.Serialization;

namespace SdevEng.Metrics;

public static class EvaluationRecordJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = false,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        return options;
    }

    public static EvaluationRecord? Deserialize(string json) =>
        JsonSerializer.Deserialize<EvaluationRecord>(json, Options);

    public static string Serialize(EvaluationRecord record) =>
        JsonSerializer.Serialize(record, Options);
}
