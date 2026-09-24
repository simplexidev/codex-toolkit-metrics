namespace CodexToolkit.Metrics;

public sealed class EvaluationRunner(IEvaluationExecutor executor, IEvaluationJudge judge)
{
    public async Task<EvaluationRunSummary> RunAsync(
        EvaluationPlan plan,
        EvaluationRunOptions options,
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var planDirectory = Path.GetDirectoryName(Path.GetFullPath(options.PlanPath))!;
        var store = new RawResultStore(options.RawDirectory);
        var summaries = new List<TrialSummary>();

        foreach (var scenario in plan.Scenarios)
        {
            var scenarioArms = scenario.ArmIds.Count == 0
                ? plan.Arms
                : plan.Arms.Where(arm => scenario.ArmIds.Contains(arm.Id, StringComparer.Ordinal)).ToArray();
            foreach (var arm in scenarioArms)
            {
                var hash = await CompatibilityHasher.ComputeAsync(
                    plan, scenario, arm, planDirectory, cancellationToken);
                var group = new List<RawTrialResult>();
                var dispositions = new List<BaselineDisposition>();
                for (var repetition = 1; repetition <= plan.Repetitions; repetition++)
                {
                    var reused = await store.TryReuseAsync(
                        plan.Suite, scenario.Id, arm.Id, repetition, hash,
                        options.ReuseBaseline, cancellationToken);
                    RawTrialResult trial;
                    if (reused.Result is not null)
                    {
                        trial = reused.Result;
                    }
                    else
                    {
                        trial = await ExecuteTrialAsync(
                            plan, scenario, arm, repetition, hash, planDirectory, cancellationToken);
                        await store.WriteTrialAsync(plan.Suite, trial, cancellationToken);
                    }

                    group.Add(trial);
                    dispositions.Add(reused.Disposition);
                }

                for (var index = 0; index < group.Count; index++)
                {
                    var trial = group[index];
                    var record = EvaluationRecordFactory.Create(plan, scenario, arm, trial, group, planDirectory);
                    var validation = EvaluationRecordValidator.Validate(record);
                    if (!validation.IsValid)
                    {
                        throw new InvalidOperationException(
                            "Runner produced an invalid evaluation record: " + string.Join("; ", validation.Errors));
                    }

                    await store.WriteRecordAsync(plan.Suite, record, cancellationToken);
                    summaries.Add(new TrialSummary
                    {
                        Scenario = scenario.Id,
                        Arm = arm.Id,
                        Repetition = trial.Repetition,
                        Passed = record.Quality.FinalQualityGate == GateOutcome.Pass,
                        TimedOut = trial.Execution.TimedOut,
                        Baseline = dispositions[index],
                        TotalTokens = trial.Execution.Usage.TotalTokens,
                        ToolCalls = trial.Execution.Usage.ToolCalls,
                        ElapsedSeconds = trial.Execution.Elapsed.TotalSeconds
                    });
                }
            }
        }

        var summary = new EvaluationRunSummary
        {
            Suite = plan.Suite,
            StartedAt = started,
            CompletedAt = DateTimeOffset.UtcNow,
            Trials = summaries
        };
        await store.WriteSummaryAsync(summary, cancellationToken);
        return summary;
    }

    private async Task<RawTrialResult> ExecuteTrialAsync(
        EvaluationPlan plan,
        EvaluationScenario scenario,
        EvaluationArmDefinition arm,
        int repetition,
        string hash,
        string planDirectory,
        CancellationToken cancellationToken)
    {
        using var workspace = IsolatedWorkspace.Create(plan.FixtureRoot, arm, planDirectory);
        var prompt = scenario.Prompt ?? await File.ReadAllTextAsync(
            CompatibilityHasher.Resolve(planDirectory, scenario.PromptFile!), cancellationToken);
        if (arm.InstructionFile is not null)
        {
            var instructions = await File.ReadAllTextAsync(
                CompatibilityHasher.Resolve(planDirectory, arm.InstructionFile), cancellationToken);
            prompt = $"<evaluator-controlled-role>\n{instructions.Trim()}\n</evaluator-controlled-role>\n\n{prompt}";
        }
        ExecutionResult execution;
        try
        {
            execution = await executor.ExecuteAsync(new ExecutionRequest(
                prompt, workspace.Path, plan.Executor, TimeSpan.FromSeconds(plan.TimeoutSeconds),
                arm.Sandbox, arm.ContextIsolated), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            execution = new ExecutionResult
            {
                ExitCode = null,
                TimedOut = false,
                Response = "",
                StandardOutput = "",
                StandardError = "",
                Elapsed = TimeSpan.Zero,
                Failure = $"Executor failure: {exception.GetType().Name}: {exception.Message}"
            };
        }

        IReadOnlyList<AssertionResult> assertions;
        try
        {
            assertions = await DeterministicAssertions.EvaluateAsync(
                scenario.Expected, execution, workspace.Path, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            assertions = [new AssertionResult("assertionRunner", false, exception.Message)];
        }

        JudgeResult judgment;
        try
        {
            judgment = await judge.JudgeAsync(
                plan.Judge, scenario, execution, assertions, workspace.Path, plan.Executor,
                TimeSpan.FromSeconds(plan.TimeoutSeconds), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            judgment = new JudgeResult
            {
                Path = plan.Judge.Path,
                Outcome = GateOutcome.Fail,
                Method = "judge failure",
                Failure = $"Judge failure: {exception.GetType().Name}: {exception.Message}"
            };
        }

        return new RawTrialResult
        {
            CompatibilityHash = hash,
            CompatibilityVersion = CompatibilityHasher.Version,
            Scenario = scenario.Id,
            Arm = arm.Id,
            Repetition = repetition,
            Timestamp = DateTimeOffset.UtcNow,
            Prompt = prompt,
            Execution = execution,
            Assertions = assertions,
            Judgment = judgment
        };
    }
}
