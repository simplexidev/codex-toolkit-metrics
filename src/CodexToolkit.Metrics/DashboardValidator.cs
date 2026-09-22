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
        }

        return errors;
    }
}
