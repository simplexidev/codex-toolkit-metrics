namespace CodexToolkit.Metrics.Tests;

public sealed class PagesPublisherTests
{
    [Fact]
    public async Task StagesOnlyManifestApprovedDashboardAndData()
    {
        using var fixture = new PublicationFixture();

        var result = await PagesPublisher.PublishAsync(fixture.Dashboard, fixture.PublicData, fixture.Output);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.True(File.Exists(Path.Combine(fixture.Output, "index.html")));
        Assert.True(File.Exists(Path.Combine(fixture.Output, "data", "approved.json")));
        Assert.True(File.Exists(Path.Combine(fixture.Output, "data", "publication-manifest.json")));
        Assert.True(File.Exists(Path.Combine(fixture.Output, ".nojekyll")));
    }

    [Fact]
    public async Task RejectsUnapprovedJsonArtifact()
    {
        using var fixture = new PublicationFixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.PublicData, "unexpected.json"), "{}");

        var result = await PagesPublisher.PublishAsync(fixture.Dashboard, fixture.PublicData, fixture.Output);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Unapproved", StringComparison.Ordinal));
        Assert.False(Directory.Exists(fixture.Output));
    }

    private sealed class PublicationFixture : IDisposable
    {
        public PublicationFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"pages-publisher-{Guid.NewGuid():N}");
            Dashboard = Path.Combine(Root, "dashboard");
            PublicData = Path.Combine(Root, "public");
            Output = Path.Combine(Root, "site");
            Directory.CreateDirectory(Dashboard);
            Directory.CreateDirectory(PublicData);
            File.WriteAllText(Path.Combine(Dashboard, "index.html"), "<link href=\"./styles.css\"><script src=\"./app.js\"></script>");
            File.WriteAllText(Path.Combine(Dashboard, "styles.css"), "body { color: white; }");
            File.WriteAllText(Path.Combine(Dashboard, "app.js"), "fetch(\"./data/publication-manifest.json\");");
            File.WriteAllText(Path.Combine(PublicData, "publication-manifest.json"), "{\"schemaVersion\":\"1.0\",\"artifacts\":[\"approved.json\"]}");
            File.WriteAllText(Path.Combine(PublicData, "approved.json"), """
                {
                  "schemaVersion": "1.0",
                  "generatedAt": "2026-09-22T00:00:00Z",
                  "subject": { "repository": "simplexidev/codex-toolkit", "revision": "0123456789abcdef0123456789abcdef01234567" },
                  "scenarios": { "total": 1, "passed": 1 },
                  "metrics": [{ "name": "fixture", "value": 1, "unit": "count", "direction": "neutral" }],
                  "provenance": { "sourceRuns": 1, "sanitized": true, "approval": "synthetic" }
                }
                """);
        }

        public string Root { get; }
        public string Dashboard { get; }
        public string PublicData { get; }
        public string Output { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
