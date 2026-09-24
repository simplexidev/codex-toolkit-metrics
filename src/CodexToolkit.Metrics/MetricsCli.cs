namespace CodexToolkit.Metrics;

public static class MetricsCli
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (args.Length >= 2 && args[0] == "measure-static")
        {
            return await MeasureStaticAsync(args, output, error, cancellationToken);
        }

        if (args.Length >= 2 && args[0] == "run")
        {
            return await RunEvaluationAsync(args, output, error, cancellationToken);
        }

        if (args.Length >= 4 && args[0] == "aggregate-baseline")
        {
            return await AggregateBaselineAsync(args, output, error, cancellationToken);
        }

        if (args.Length >= 5 && args[0] == "aggregate-agent-capability")
        {
            return await AggregateAgentCapabilityAsync(args, output, error, cancellationToken);
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

        if (args.Length == 4 && args[0] == "validate-agent-candidates")
        {
            var result = await ToolkitMetadataValidator.ValidateAsync(args[1], args[2], args[3], cancellationToken);
            if (result.IsValid)
            {
                await output.WriteLineAsync("Agent candidate plan and recommendations align with toolkit metadata.");
                return 0;
            }
            foreach (var validationError in result.Errors) await error.WriteLineAsync(validationError);
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

        if (args.Length == 4 && args[0] == "publish-pages")
        {
            var result = await PagesPublisher.PublishAsync(args[1], args[2], args[3], cancellationToken);
            if (result.IsValid)
            {
                await output.WriteLineAsync($"Sanitized Pages artifact staged at: {Path.GetFullPath(args[3])}");
                return 0;
            }

            foreach (var validationError in result.Errors) await error.WriteLineAsync(validationError);
            return 1;
        }

        await output.WriteLineAsync("Codex Toolkit Metrics");
        await output.WriteLineAsync("  run <plan.json> [--raw-dir <directory>] [--reuse-baseline]");
        await output.WriteLineAsync("  aggregate-baseline <raw-directory> <output.json> <toolkit-revision> [--generated-at <timestamp>]");
        await output.WriteLineAsync("  aggregate-agent-capability <raw-directory> <recommendations.json> <output.json> <toolkit-revision> [--generated-at <timestamp>]");
        await output.WriteLineAsync("  validate-plan <plan.json>");
        await output.WriteLineAsync("  validate-agent-candidates <toolkit-directory> <plan.json> <recommendations.json>");
        await output.WriteLineAsync("  validate-evaluation <record.json>");
        await output.WriteLineAsync("  validate-public <aggregate.json>");
        await output.WriteLineAsync("  dashboard-check <dashboard-directory>");
        await output.WriteLineAsync("  publish-pages <dashboard-directory> <public-data-directory> <output-directory>");
        await output.WriteLineAsync("  measure-static <toolkit-directory> [--output <report.json>] [--public-output <aggregate.json>]");
        return args.Length == 0 || (args.Length == 1 && args[0] is "help" or "--help" or "-h") ? 0 : 2;
    }

    private static async Task<int> AggregateAgentCapabilityAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        for (var index = 5; index < args.Length; index++)
        {
            if (args[index] == "--generated-at" && index + 1 < args.Length &&
                DateTimeOffset.TryParse(args[++index], out var parsed)) generatedAt = parsed;
            else
            {
                await error.WriteLineAsync($"Unknown, incomplete, or invalid aggregate-agent-capability option: {args[index]}");
                return 2;
            }
        }

        try
        {
            var json = await AgentCapabilityAggregator.AggregateAsync(
                args[1], args[2], args[4], generatedAt, cancellationToken);
            await WriteAsync(args[3], json, cancellationToken);
            var validation = await PublicMetricsValidator.ValidateFileAsync(args[3], cancellationToken);
            if (!validation.IsValid)
            {
                foreach (var validationError in validation.Errors) await error.WriteLineAsync(validationError);
                return 1;
            }
            await output.WriteLineAsync($"Sanitized agent/capability aggregate written to: {Path.GetFullPath(args[3])}");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync($"Agent/capability aggregation failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> AggregateBaselineAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        for (var index = 4; index < args.Length; index++)
        {
            if (args[index] == "--generated-at" && index + 1 < args.Length &&
                DateTimeOffset.TryParse(args[++index], out var parsed))
            {
                generatedAt = parsed;
            }
            else
            {
                await error.WriteLineAsync($"Unknown, incomplete, or invalid aggregate-baseline option: {args[index]}");
                return 2;
            }
        }

        try
        {
            var json = await PreOptimizationBaselineAggregator.AggregateAsync(
                args[1], args[3], generatedAt, cancellationToken);
            await WriteAsync(args[2], json, cancellationToken);
            var validation = await PublicMetricsValidator.ValidateFileAsync(args[2], cancellationToken);
            if (!validation.IsValid)
            {
                foreach (var validationError in validation.Errors) await error.WriteLineAsync(validationError);
                return 1;
            }
            await output.WriteLineAsync($"Sanitized reviewed baseline written to: {Path.GetFullPath(args[2])}");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync($"Baseline aggregation failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> MeasureStaticAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        string? reportPath = null;
        string? publicPath = null;
        for (var index = 2; index < args.Length; index++)
        {
            if (args[index] is "--output" or "--public-output" && index + 1 < args.Length)
            {
                if (args[index] == "--output") reportPath = args[++index];
                else publicPath = args[++index];
            }
            else
            {
                await error.WriteLineAsync($"Unknown or incomplete measure-static option: {args[index]}");
                return 2;
            }
        }

        try
        {
            var report = StaticCostAnalyzer.Analyze(args[1]);
            var json = System.Text.Json.JsonSerializer.Serialize(report, EvaluationRecordJson.Options);
            if (reportPath is null) await output.WriteLineAsync(json);
            else await WriteAsync(reportPath, json, cancellationToken);
            if (publicPath is not null)
            {
                await WriteAsync(publicPath, StaticCostAnalyzer.PublicAggregate(report, DateTimeOffset.UtcNow), cancellationToken);
            }
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync($"Static measurement failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task WriteAsync(string path, string contents, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, contents + Environment.NewLine, cancellationToken);
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
