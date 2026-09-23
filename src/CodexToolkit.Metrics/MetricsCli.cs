namespace CodexToolkit.Metrics;

public static class MetricsCli
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (args.Length >= 2 && args[0] == "run")
        {
            return await RunEvaluationAsync(args, output, error, cancellationToken);
        }

        if (args.Length == 2 && args[0] == "validate-plan")
        {
            var loaded = await EvaluationPlanLoader.LoadAsync(args[1], cancellationToken);
            if (loaded.Plan is not null && loaded.Errors.Count == 0)
            {
                await output.WriteLineAsync($"Valid evaluation plan: {args[1]}");
                return 0;
            }

            foreach (var validationError in loaded.Errors) await error.WriteLineAsync(validationError);
            return 1;
        }

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

        if (args.Length == 2 && args[0] == "validate-evaluation")
        {
            var result = await EvaluationRecordValidator.ValidateFileAsync(args[1], cancellationToken);
            if (result.IsValid)
            {
                await output.WriteLineAsync($"Valid evaluation record: {args[1]}");
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
        await output.WriteLineAsync("  run <plan.json> [--raw-dir <directory>] [--reuse-baseline]");
        await output.WriteLineAsync("  validate-plan <plan.json>");
        await output.WriteLineAsync("  validate-evaluation <record.json>");
        await output.WriteLineAsync("  validate-public <aggregate.json>");
        await output.WriteLineAsync("  dashboard-check <dashboard-directory>");
        return args.Length == 0 || (args.Length == 1 && args[0] is "help" or "--help" or "-h") ? 0 : 2;
    }

    private static async Task<int> RunEvaluationAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        string? rawDirectory = null;
        var reuse = false;
        for (var index = 2; index < args.Length; index++)
        {
            if (args[index] == "--reuse-baseline")
            {
                reuse = true;
            }
            else if (args[index] == "--raw-dir" && index + 1 < args.Length)
            {
                rawDirectory = args[++index];
            }
            else
            {
                await error.WriteLineAsync($"Unknown or incomplete run option: {args[index]}");
                return 2;
            }
        }

        var planPath = Path.GetFullPath(args[1]);
        var loaded = await EvaluationPlanLoader.LoadAsync(planPath, cancellationToken);
        if (loaded.Plan is null || loaded.Errors.Count > 0)
        {
            foreach (var validationError in loaded.Errors) await error.WriteLineAsync(validationError);
            return 2;
        }

        rawDirectory ??= Path.Combine(Environment.CurrentDirectory, "data", "private", "runs");
        try
        {
            var executor = new CodexCliExecutor();
            var runner = new EvaluationRunner(executor, new EvaluationJudge(executor));
            var summary = await runner.RunAsync(
                loaded.Plan,
                new EvaluationRunOptions(planPath, rawDirectory, reuse),
                cancellationToken);
            await output.WriteLineAsync(
                $"Suite {summary.Suite}: {summary.Passed} passed, {summary.Failed} failed, " +
                $"{summary.Trials.Count(trial => trial.Baseline == BaselineDisposition.Reused)} baselines reused.");
            await output.WriteLineAsync($"Private raw results: {Path.GetFullPath(rawDirectory)}");
            return summary.Failed == 0 ? 0 : 1;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync($"Evaluation run failed: {exception.Message}");
            return 1;
        }
    }
}
