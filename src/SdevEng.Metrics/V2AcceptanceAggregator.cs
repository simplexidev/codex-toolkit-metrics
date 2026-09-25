using System.Text.Json;

namespace SdevEng.Metrics;

public static class V2AcceptanceAggregator
{
    public static async Task<string> AggregateAsync(
        string rawDirectory,
        string planPath,
        string baselinePath,
        string capabilityManifestPath,
        string toolkitRevision,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateRevision(toolkitRevision);
        var loaded = await EvaluationPlanLoader.LoadAsync(planPath, cancellationToken);
        if (loaded.Plan is null || loaded.Errors.Count != 0) throw new InvalidOperationException("Acceptance plan is invalid: " + string.Join("; ", loaded.Errors));
        var plan = loaded.Plan;
        if (plan.Baseline is null) throw new InvalidOperationException("Acceptance plan must bind a reviewed baseline identity.");
        if (!string.Equals(plan.Provenance.Toolkit.Sha, toolkitRevision, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Acceptance plan and requested toolkit revision do not match.");
        var capabilityIds = await LoadCapabilityIdsAsync(capabilityManifestPath, cancellationToken);
        var unknown = plan.Scenarios.Select(s => s.Capability).Where(id => !capabilityIds.Contains(id)).Distinct().ToArray();
        if (unknown.Length != 0) throw new InvalidOperationException("Acceptance plan contains unknown product capability IDs: " + string.Join(", ", unknown));
        var records = await LoadRecordsAsync(rawDirectory, toolkitRevision, cancellationToken);
        await ValidateExactMatrixAsync(plan, records, planPath, cancellationToken);
        if (records.Any(record => record.Arm.Comparison != ComparisonArm.Optimized ||
                                  record.Arm.Customization != CustomizationArm.OptimizedOrNew))
            throw new InvalidOperationException("V2 acceptance records must use the OPTIMIZED / OPTIMIZED-or-NEW arm.");

        using var baseline = JsonDocument.Parse(await File.ReadAllTextAsync(baselinePath, cancellationToken));
        ValidateBaseline(baseline.RootElement, plan.Baseline);
        var baselineMetrics = baseline.RootElement.GetProperty("metrics").EnumerateArray()
            .ToDictionary(item => item.GetProperty("name").GetString()!, item => item.GetProperty("value").GetDecimal(), StringComparer.Ordinal);
        var baselineScenarios = baseline.RootElement.GetProperty("scenarios");
        var baselineFailures = baselineScenarios.GetProperty("total").GetInt32() - baselineScenarios.GetProperty("passed").GetInt32();

        var scenarios = records.GroupBy(record => record.Identity.Scenario, StringComparer.Ordinal).ToArray();
        var passed = scenarios.Count(group => group.All(record => record.Quality.FinalQualityGate == GateOutcome.Pass));
        var failures = scenarios.Length - passed;
        var skillRecords = records.Where(record => !record.Identity.Capability.Contains("agent", StringComparison.Ordinal)).ToArray();
        var agentRecords = records.Except(skillRecords).ToArray();
        var metrics = new List<PublicAcceptanceMetric>
        {
            Metric("scenario-pass-rate", Ratio(passed, scenarios.Length), "ratio", "higher-is-better", "measured", "optimized v2 scenarios passing the final quality gate"),
            Metric("regression-count", Math.Max(0, failures - baselineFailures), "count", "lower-is-better", "derived", "optimized failures above the reviewed pre-v2 baseline failure count"),
            Metric("model-policy-compliance", records.All(IsOpenAiRecord) ? 1 : 0, "ratio", "higher-is-better", "derived", "validated OpenAI executor identities and deterministic judge path"),
            Metric("acceptance-scenario-coverage", Ratio(scenarios.Length, plan.Scenarios.Count), "ratio", "higher-is-better", "derived", "verified completed scenario matrix divided by the bounded acceptance plan"),
            Metric("capability-coverage", Ratio(plan.Scenarios.Select(s => s.Capability).Distinct(StringComparer.Ordinal).Count(), capabilityIds.Count), "ratio", "higher-is-better", "derived", "distinct validated product capability IDs divided by the subject capability manifest"),
            Metric("pre-v2-scenario-pass-rate", Baseline(baselineMetrics, "scenario-pass-rate"), "ratio", "higher-is-better", "measured", "reviewed pre-v2 baseline"),
            Metric("vanilla-skill-pass-rate", Baseline(baselineMetrics, "dotnet-vanilla-pass-rate"), "ratio", "higher-is-better", "measured", "reviewed VANILLA baseline"),
            Metric("upstream-skill-pass-rate", Baseline(baselineMetrics, "dotnet-upstream-pass-rate"), "ratio", "higher-is-better", "measured", "reviewed UPSTREAM baseline"),
            Metric("optimized-skill-pass-rate", PassRate(skillRecords), "ratio", "higher-is-better", "measured", "optimized capability records passing the final quality gate"),
            Metric("optimized-agent-pass-rate", PassRate(agentRecords), "ratio", "higher-is-better", "measured", "optimized agent-boundary records passing the final quality gate"),
            Metric("deterministic-resolution-rate", Ratio(records.Count(record => record.Jev.DeterministicResolutions.Value > 0), records.Length), "ratio", "higher-is-better", "derived", "records resolved by deterministic assertions without a semantic judge"),
            Metric("jev-remote-calls", Sum(records, record => record.Efficiency.JevJudgeCalls), "calls", "lower-is-better", "measured", "instrumented remote JEV judge calls"),
            Metric("jev-fallbacks", Sum(records, record => record.Jev.Fallbacks), "count", "lower-is-better", "measured", "instrumented JEV fallbacks"),
            Metric("jev-escalations", Sum(records, record => record.Jev.Escalations), "count", "lower-is-better", "measured", "instrumented JEV-to-GPT escalations"),
            Metric("jev-false-exclusion-coverage", Availability(records, record => record.Jev.FalseExclusions), "ratio", "higher-is-better", "derived", "records with calibrated false-exclusion evidence"),
            Metric("jev-context-avoided-coverage", Availability(records, record => record.Jev.ContextAvoided), "ratio", "higher-is-better", "derived", "records with measured avoided-context evidence")
        };
        AddMean(metrics, "optimized", "total-tokens", records, record => record.Efficiency.GptTotalTokens, "tokens");
        AddMean(metrics, "optimized", "elapsed-seconds", records, record => record.Efficiency.ElapsedTime, "seconds");
        AddMean(metrics, "optimized", "tool-calls", records, record => record.Efficiency.ToolCalls, "count");
        AddMean(metrics, "optimized-skill", "total-tokens", skillRecords, record => record.Efficiency.GptTotalTokens, "tokens");
        AddMean(metrics, "optimized-skill", "elapsed-seconds", skillRecords, record => record.Efficiency.ElapsedTime, "seconds");
        AddMean(metrics, "optimized-agent", "total-tokens", agentRecords, record => record.Efficiency.GptTotalTokens, "tokens");
        AddMean(metrics, "optimized-agent", "elapsed-seconds", agentRecords, record => record.Efficiency.ElapsedTime, "seconds");
        AddMean(metrics, "optimized", "files-in-context", records, record => record.Efficiency.FilesInContext, "count");
        AddMean(metrics, "optimized", "context-tokens", records, record => record.Efficiency.ContextTokens, "tokens");
        AddMean(metrics, "optimized", "full-builds", records, record => record.Efficiency.FullBuilds, "count");
        AddMean(metrics, "optimized", "targeted-builds", records, record => record.Efficiency.TargetedBuilds, "count");
        AddMean(metrics, "optimized", "full-tests", records, record => record.Efficiency.FullTests, "count");
        AddMean(metrics, "optimized", "targeted-tests", records, record => record.Efficiency.TargetedTests, "count");

        var document = new PublicAcceptanceDocument(
            "1.0", generatedAt,
            new PublicAcceptanceSubject("simplexidev/sdeveng", toolkitRevision.ToLowerInvariant()),
            new PublicAcceptanceScenarios(scenarios.Length, passed), metrics,
            new PublicAcceptanceProvenance(records.Length, true, "reviewed"));
        return JsonSerializer.Serialize(document, EvaluationRecordJson.Options);
    }

    private static async Task<EvaluationRecord[]> LoadRecordsAsync(string rawDirectory, string revision, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(rawDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var paths = Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(path => path.Replace(Path.DirectorySeparatorChar, '/').Contains("/records/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        if (paths.Length == 0) throw new InvalidOperationException("No private evaluation records were found beneath a records directory.");
        var records = new List<EvaluationRecord>();
        foreach (var path in paths)
        {
            var record = EvaluationRecordJson.Deserialize(await File.ReadAllTextAsync(path, cancellationToken))
                ?? throw new InvalidOperationException("An evaluation record was empty or null.");
            var validation = EvaluationRecordValidator.Validate(record);
            if (!validation.IsValid) throw new InvalidOperationException("A private evaluation record was invalid: " + string.Join("; ", validation.Errors));
            if (!string.Equals(record.Identity.Toolkit.Sha, revision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Evaluation records and requested toolkit revision do not match.");
            records.Add(record);
        }
        return records.ToArray();
    }

    private static bool IsOpenAiRecord(EvaluationRecord record) =>
        string.Equals(record.Identity.Executor.Provider, "openai", StringComparison.OrdinalIgnoreCase) && record.Identity.Executor.Model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) &&
        record.Identity.Judge.Method == JudgeMethod.Deterministic;

    private static async Task<HashSet<string>> LoadCapabilityIdsAsync(string path, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        if (!document.RootElement.TryGetProperty("capabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Capability manifest must contain a capabilities array.");
        var ids = capabilities.EnumerateArray().Select(item => item.GetProperty("id").GetString()).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0) throw new InvalidOperationException("Capability manifest contains no capability IDs.");
        return ids!;
    }

    private static async Task ValidateExactMatrixAsync(EvaluationPlan plan, IReadOnlyList<EvaluationRecord> records, string planPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(planPath))!;
        var expected = new Dictionary<(string Scenario, string Arm, int Repetition), string>();
        foreach (var scenario in plan.Scenarios) foreach (var arm in scenario.ArmIds.Count == 0 ? plan.Arms : plan.Arms.Where(arm => scenario.ArmIds.Contains(arm.Id, StringComparer.Ordinal))) for (var repetition = 1; repetition <= plan.Repetitions; repetition++) expected[(scenario.Id, arm.Id, repetition)] = await CompatibilityHasher.ComputeAsync(plan, scenario, arm, directory, cancellationToken);
        var actual = records.GroupBy(record => (record.Identity.Scenario, record.Identity.Arm, record.Identity.Repetition)).ToArray();
        if (actual.Any(group => group.Count() != 1) || actual.Length != expected.Count || actual.Any(group => !expected.ContainsKey(group.Key))) throw new InvalidOperationException("Acceptance records must contain exactly one record for every planned scenario, arm, and repetition, with no duplicates or unexpected tuples.");
        foreach (var group in actual) if (!string.Equals(group.Single().Statistics.BaselineCompatibility.Hash, expected[group.Key], StringComparison.Ordinal)) throw new InvalidOperationException($"Acceptance record compatibility hash does not match the reviewed plan for {group.Key}.");
    }

    private static void ValidateBaseline(JsonElement root, AcceptanceBaseline expected)
    {
        var repository = root.GetProperty("subject").GetProperty("repository").GetString();
        if (root.GetProperty("schemaVersion").GetString() != "1.0" || repository is not ("simplexidev/sdeveng" or "simplexidev/codex-toolkit") || !string.Equals(root.GetProperty("subject").GetProperty("revision").GetString(), expected.Subject.Sha, StringComparison.OrdinalIgnoreCase) || root.GetProperty("provenance").GetProperty("approval").GetString() != expected.Approval) throw new InvalidOperationException("Baseline does not match the acceptance plan's reviewed schema, subject revision, and approval lineage.");
    }

    private static decimal Baseline(IReadOnlyDictionary<string, decimal> metrics, string name) =>
        metrics.TryGetValue(name, out var value) ? value : throw new InvalidOperationException($"Required baseline metric is missing: {name}.");

    private static decimal PassRate(IReadOnlyCollection<EvaluationRecord> records) =>
        records.Count == 0 ? 0 : Ratio(records.Count(record => record.Quality.FinalQualityGate == GateOutcome.Pass), records.Count);

    private static decimal Sum(IEnumerable<EvaluationRecord> records, Func<EvaluationRecord, NumericMetric?> selector) =>
        records.Select(selector).Where(metric => metric?.Value is not null).Sum(metric => metric!.Value!.Value);

    private static decimal Availability(IReadOnlyCollection<EvaluationRecord> records, Func<EvaluationRecord, NumericMetric> selector) =>
        Ratio(records.Count(record => selector(record).Kind != MeasurementKind.Unavailable), records.Count);

    private static void AddMean(List<PublicAcceptanceMetric> metrics, string prefix, string suffix,
        IReadOnlyCollection<EvaluationRecord> records, Func<EvaluationRecord, NumericMetric> selector, string unit)
    {
        var available = records.Select(selector).Where(metric => metric.Value.HasValue).Select(metric => metric.Value!.Value).ToArray();
        if (available.Length == 0) return;
        metrics.Add(Metric($"{prefix}-mean-{suffix}", decimal.Round(available.Average(), 4), unit,
            suffix is "total-tokens" or "elapsed-seconds" ? "lower-is-better" : "neutral", "derived", "arithmetic mean over available optimized records"));
    }

    private static decimal Ratio(int numerator, int denominator) => denominator == 0 ? 0 : decimal.Round((decimal)numerator / denominator, 4);
    private static PublicAcceptanceMetric Metric(string name, decimal value, string unit, string direction, string kind, string method) =>
        new(name, value, unit, direction, kind, method);
    private static void ValidateRevision(string revision)
    {
        if (revision.Length is < 7 or > 64 || !revision.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Toolkit revision must be a 7-64 character hexadecimal revision.");
    }

    private sealed record PublicAcceptanceDocument(string SchemaVersion, DateTimeOffset GeneratedAt, PublicAcceptanceSubject Subject,
        PublicAcceptanceScenarios Scenarios, IReadOnlyList<PublicAcceptanceMetric> Metrics, PublicAcceptanceProvenance Provenance);
    private sealed record PublicAcceptanceSubject(string Repository, string Revision);
    private sealed record PublicAcceptanceScenarios(int Total, int Passed);
    private sealed record PublicAcceptanceMetric(string Name, decimal Value, string Unit, string Direction, string Kind, string Method);
    private sealed record PublicAcceptanceProvenance(int SourceRuns, bool Sanitized, string Approval);
}
