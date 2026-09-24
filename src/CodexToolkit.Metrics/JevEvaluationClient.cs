using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexToolkit.Metrics;

public enum JevDisposition { Accept, Review }

public sealed record JevEvaluationRequest(string State, string Rubric);

public sealed record JevEvaluationResult
{
    public required JevDisposition Disposition { get; init; }
    public decimal? Score { get; init; }
    public decimal? Confidence { get; init; }
    public required string Reason { get; init; }
    public JevUsage Usage { get; init; } = new();
}

public interface IJevEvaluationClient
{
    Task<JevEvaluationResult> EvaluateAsync(JevEvaluationRequest request, JevJudgeConfiguration configuration, CancellationToken cancellationToken);
}

public sealed class HttpJevEvaluationClient(HttpClient http, Func<string?>? credentialSource = null) : IJevEvaluationClient
{
    public const string CredentialEnvironmentVariable = "TYPESAFE_API_KEY";
    private readonly Func<string?> _credentialSource = credentialSource ?? (() => Environment.GetEnvironmentVariable(CredentialEnvironmentVariable));

    public async Task<JevEvaluationResult> EvaluateAsync(JevEvaluationRequest request, JevJudgeConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(configuration.ApiUrl, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps || endpoint.UserInfo.Length != 0 ||
            endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            return Review("JEV endpoint must be credential-free HTTPS", 0);
        var body = BuildRequest(request, configuration.Model).ToJsonString();
        if (Encoding.UTF8.GetByteCount(body) > configuration.MaxInputBytes) return Review("bounded JEV input exceeds configured byte limit", 0);
        if (ContainsSensitiveMarker(body)) return Review("bounded JEV input failed the secret safety check", 0);
        var credential = _credentialSource();
        if (string.IsNullOrWhiteSpace(credential)) return Review("JEV credential unavailable", 0);

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, configuration.ApiUrl);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
            message.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(configuration.TimeoutSeconds));
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return Review($"JEV returned HTTP {(int)response.StatusCode}; body withheld", 1);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var memory = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (memory.Length + count > 65_536) return Review("JEV response exceeded 65536 bytes", 1);
                await memory.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
            }
            using var document = JsonDocument.Parse(memory.ToArray());
            return Parse(document.RootElement, configuration.MinConfidence);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
            return Review("JEV unavailable or returned an invalid response", 1);
        }
    }

    internal static JsonObject BuildRequest(JevEvaluationRequest request, string model) => new()
    {
        ["model"] = model,
        ["state"] = request.State,
        ["questions"] = new JsonObject
        {
            ["judgment"] = new JsonObject
            {
                ["type"] = "score",
                ["instructions"] = request.Rubric,
                ["criteria"] = new JsonArray("does not satisfy", "mostly does not satisfy", "partly satisfies", "mostly satisfies", "fully satisfies")
            }
        }
    };

    internal static JevEvaluationResult Parse(JsonElement root, decimal minConfidence)
    {
        var answer = root.GetProperty("answers").GetProperty("judgment");
        if (answer.GetProperty("type").GetString() != "score") throw new JsonException("Wrong answer type.");
        var score = answer.GetProperty("score").GetDecimal();
        var confidence = answer.GetProperty("confidence").GetDecimal();
        if (score is < 0 or > 4 || confidence is < 0 or > 1) throw new JsonException("JEV numeric answer out of range.");
        var weightedScore = ValidateProbabilities(answer.GetProperty("probabilities"));
        if (Math.Abs(weightedScore - score) > 0.02m) throw new JsonException("JEV score and distribution disagree.");
        ValidateLegend(answer.GetProperty("legend"));
        return new JevEvaluationResult
        {
            Disposition = confidence >= minConfidence ? JevDisposition.Accept : JevDisposition.Review,
            Score = score / 4m,
            Confidence = confidence,
            Reason = confidence >= minConfidence ? "bounded JEV score accepted" : "JEV confidence below configured minimum",
            Usage = new JevUsage { Invocations = 1, RemoteCalls = 1 }
        };
    }

    private static decimal ValidateProbabilities(JsonElement probabilities)
    {
        if (probabilities.ValueKind != JsonValueKind.Object) throw new JsonException("Missing probability distribution.");
        decimal sum = 0;
        decimal weighted = 0;
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var probability in probabilities.EnumerateObject())
        {
            if (!labels.Add(probability.Name) || probability.Name is not ("0" or "1" or "2" or "3" or "4") ||
                !probability.Value.TryGetDecimal(out var value) || value is < 0 or > 1) throw new JsonException("Invalid probability distribution.");
            sum += value;
            weighted += int.Parse(probability.Name, System.Globalization.CultureInfo.InvariantCulture) * value;
        }
        if (labels.Count != 5 || Math.Abs(sum - 1m) > 0.01m) throw new JsonException("Invalid probability distribution.");
        return weighted;
    }

    private static void ValidateLegend(JsonElement legend)
    {
        if (legend.ValueKind != JsonValueKind.Object) throw new JsonException("Missing score legend.");
        var labels = legend.EnumerateObject().ToArray();
        if (labels.Length != 5 || labels.Any(item => item.Name is not ("0" or "1" or "2" or "3" or "4") ||
            item.Value.ValueKind != JsonValueKind.String)) throw new JsonException("Invalid score legend.");
    }

    private static bool ContainsSensitiveMarker(string value) =>
        value.Contains(CredentialEnvironmentVariable, StringComparison.OrdinalIgnoreCase) ||
        value.Contains("-----BEGIN PRIVATE KEY-----", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("password=", StringComparison.OrdinalIgnoreCase) || value.Contains("api_key=", StringComparison.OrdinalIgnoreCase);

    private static JevEvaluationResult Review(string reason, int remoteCalls) => new()
    {
        Disposition = JevDisposition.Review,
        Reason = reason,
        Usage = new JevUsage { Invocations = 1, RemoteCalls = remoteCalls, Fallbacks = 1 }
    };
}
