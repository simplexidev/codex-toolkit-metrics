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
}
