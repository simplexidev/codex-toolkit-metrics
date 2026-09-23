namespace CodexToolkit.Metrics;

public static class DashboardValidator
{
    private static readonly string[] RequiredFiles = ["index.html", "styles.css", "app.js"];

    public static IReadOnlyList<string> Validate(string dashboardDirectory)
    {
        var errors = new List<string>();
        foreach (var file in RequiredFiles)
        {
            var path = Path.Combine(dashboardDirectory, file);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                errors.Add($"Missing or empty dashboard source file: {path}");
            }
        }

        var indexPath = Path.Combine(dashboardDirectory, "index.html");
        if (File.Exists(indexPath))
        {
            var index = File.ReadAllText(indexPath);
            if (!index.Contains("styles.css", StringComparison.Ordinal) ||
                !index.Contains("app.js", StringComparison.Ordinal))
            {
                errors.Add("Dashboard index must reference styles.css and app.js.");
            }

            if (index.Contains("href=\"/", StringComparison.Ordinal) ||
                index.Contains("src=\"/", StringComparison.Ordinal))
            {
                errors.Add("Dashboard assets must use project-relative paths for GitHub Pages.");
            }
        }


        var appPath = Path.Combine(dashboardDirectory, "app.js");
        if (File.Exists(appPath) && File.ReadAllText(appPath).Contains("fetch(\"/", StringComparison.Ordinal))
        {
            errors.Add("Dashboard data requests must use project-relative paths for GitHub Pages.");
        }

        return errors;
    }
}
