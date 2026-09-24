using System.Net;
using System.Text;

namespace CodexToolkit.Metrics.Tests;

public sealed class JevEvaluationClientTests
{
    private const string ValidResponse = """
        {"answers":{"judgment":{"type":"score","score":3.2,"confidence":0.9,
        "probabilities":{"0":0.01,"1":0.04,"2":0.1,"3":0.45,"4":0.4},
        "legend":{"0":"does not satisfy","1":"mostly does not satisfy","2":"partly satisfies","3":"mostly satisfies","4":"fully satisfies"}}}}
        """;

    [Fact]
    public async Task ParsesBoundedScoreAndReadsCredentialOnlyAtHttpBoundary()
    {
        var handler = new RecordingHandler(ValidResponse);
        using var http = new HttpClient(handler);
        var reads = 0;
        var result = await new HttpJevEvaluationClient(http, () => { reads++; return "synthetic-test-key"; })
            .EvaluateAsync(new JevEvaluationRequest("public answer", "rate quality"), new JevJudgeConfiguration(), CancellationToken.None);

        Assert.Equal(JevDisposition.Accept, result.Disposition);
        Assert.Equal(0.8m, result.Score);
        Assert.Equal(0.9m, result.Confidence);
        Assert.Equal(1, reads);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.DoesNotContain("synthetic-test-key", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingCredentialReturnsReviewWithoutHttpCall()
    {
        var handler = new RecordingHandler(ValidResponse);
        using var http = new HttpClient(handler);
        var result = await new HttpJevEvaluationClient(http, () => null)
            .EvaluateAsync(new JevEvaluationRequest("public answer", "rate quality"), new JevJudgeConfiguration(), CancellationToken.None);

        Assert.Equal(JevDisposition.Review, result.Disposition);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(1, result.Usage.Fallbacks);
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Body { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
