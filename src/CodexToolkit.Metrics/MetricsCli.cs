namespace CodexToolkit.Metrics;

public static class MetricsCli
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (args.Length == 2 && args[0] == "validate-public")
        {
            var result = await PublicMetricsValidator.ValidateFileAsync(args[1], cancellationToken);
            if (result.IsValid)
            {
                await output.WriteLineAsync($"Valid sanitized public metrics: {args[1]}");
                return 0;
            }

            foreach (var validationError in result.Errors)
            {
                await error.WriteLineAsync(validationError);
            }

            return 1;
        }

        if (args.Length == 2 && args[0] == "dashboard-check")
        {
            var errors = DashboardValidator.Validate(args[1]);
            if (errors.Count == 0)
            {
                await output.WriteLineAsync($"Dashboard source is complete: {args[1]}");
                return 0;
            }

            foreach (var validationError in errors)
            {
                await error.WriteLineAsync(validationError);
            }

            return 1;
        }

        await output.WriteLineAsync("Codex Toolkit Metrics");
        await output.WriteLineAsync("  validate-public <aggregate.json>");
        await output.WriteLineAsync("  dashboard-check <dashboard-directory>");
        return args.Length == 0 || (args.Length == 1 && args[0] is "help" or "--help" or "-h") ? 0 : 2;
    }
}
