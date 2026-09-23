using System.Text.Json;

namespace CodexToolkit.Metrics;

public sealed class RawResultStore
{
    private readonly string _root;

    public RawResultStore(string root)
    {
        _root = Path.GetFullPath(root);
        var normalized = _root.Replace(Path.DirectorySeparatorChar, '/').TrimEnd('/') + "/";
        if (normalized.Contains("/data/public/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Raw evaluation results may not be stored under data/public.");
        }
    }

    public string Root => _root;

    public async Task<(RawTrialResult? Result, BaselineDisposition Disposition)> TryReuseAsync(
        string suite,
        string scenario,
        string arm,
        int repetition,
        string compatibilityHash,
        bool requested,
        CancellationToken cancellationToken)
    {
        if (!requested) return (null, BaselineDisposition.NotRequested);
        var path = TrialPath(suite, scenario, arm, repetition);
        if (!File.Exists(path)) return (null, BaselineDisposition.Missing);

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var result = JsonSerializer.Deserialize<RawTrialResult>(json, EvaluationRecordJson.Options);
            return result is not null &&
                   result.CompatibilityVersion == CompatibilityHasher.Version &&
                   result.CompatibilityHash == compatibilityHash &&
                   result.Scenario == scenario && result.Arm == arm && result.Repetition == repetition &&
                   result.Execution is not null && result.Assertions is not null && result.Judgment is not null
                ? (result, BaselineDisposition.Reused)
                : (null, BaselineDisposition.StaleRejected);
        }
        catch (JsonException)
        {
            return (null, BaselineDisposition.StaleRejected);
        }
    }

    public Task WriteTrialAsync(string suite, RawTrialResult result, CancellationToken cancellationToken) =>
        WriteJsonAsync(TrialPath(suite, result.Scenario, result.Arm, result.Repetition), result, cancellationToken);

    public Task WriteRecordAsync(
        string suite,
        EvaluationRecord record,
        CancellationToken cancellationToken) =>
        WriteJsonAsync(Path.Combine(_root, Segment(suite), "records", Segment(record.Identity.Scenario),
            Segment(record.Identity.Arm), $"{record.Identity.Repetition}.json"), record, cancellationToken);

    public Task WriteSummaryAsync(EvaluationRunSummary summary, CancellationToken cancellationToken) =>
        WriteJsonAsync(Path.Combine(_root, Segment(summary.Suite), "summary.json"), summary, cancellationToken);

    private string TrialPath(string suite, string scenario, string arm, int repetition) =>
        Path.Combine(_root, Segment(suite), "trials", Segment(scenario), Segment(arm), $"{repetition}.raw.json");

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(value, EvaluationRecordJson.Options),
                cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string Segment(string value)
    {
        var valid = value.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-').ToArray();
        var segment = new string(valid).Trim('.', '-');
        if (string.IsNullOrWhiteSpace(segment)) throw new InvalidOperationException("Result identifiers require a safe path segment.");
        return segment;
    }
}
