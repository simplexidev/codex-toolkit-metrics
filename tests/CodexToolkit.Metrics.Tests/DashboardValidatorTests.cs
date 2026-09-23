namespace CodexToolkit.Metrics.Tests;

public sealed class DashboardValidatorTests
{
    [Fact]
    public void RejectsAnIncompleteDashboard()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"codex-toolkit-metrics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(Path.Combine(directory, "index.html"), "<html></html>");

            var errors = DashboardValidator.Validate(directory);

            Assert.Contains(errors, error => error.Contains("styles.css", StringComparison.Ordinal));
            Assert.Contains(errors, error => error.Contains("app.js", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsRootRelativeAssetsThatBreakProjectPages()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"codex-toolkit-metrics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "index.html"), "<link href=\"/styles.css\"><script src=\"/app.js\"></script>");
            File.WriteAllText(Path.Combine(directory, "styles.css"), "body {}");
            File.WriteAllText(Path.Combine(directory, "app.js"), "fetch(\"/data/metrics.json\")");

            var errors = DashboardValidator.Validate(directory);

            Assert.Contains(errors, error => error.Contains("project-relative", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
