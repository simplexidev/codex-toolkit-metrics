using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SdevEng.Metrics;

public sealed record TextCost
{
    public required long Bytes { get; init; }
    public required long Characters { get; init; }
    public required long Tokens { get; init; }
    public string TokenKind { get; init; } = "estimated";
    public string TokenMethod { get; init; } = StaticCostAnalyzer.TokenMethod;
}

public sealed record SkillCost
{
    public required string Name { get; init; }
    public required TextCost Trigger { get; init; }
    public required TextCost SkillFile { get; init; }
    public required TextCost LazyReferences { get; init; }
    public required TextCost MaximumPossibleLoad { get; init; }
    public required TextCost AlwaysVisible { get; init; }
    public required TextCost ActivationVisible { get; init; }
    public required int LazyReferenceFiles { get; init; }
}

public sealed record AgentCost
{
    public required string Name { get; init; }
    public required TextCost Configuration { get; init; }
    public required TextCost Instructions { get; init; }
    public required TextCost Total { get; init; }
}

public sealed record RoutingOverlap
{
    public required string Left { get; init; }
    public required string Right { get; init; }
    public required decimal Jaccard { get; init; }
    public required int SharedTerms { get; init; }
}

public sealed record StaticCostSummary
{
    public required int SkillCount { get; init; }
    public required int AgentCount { get; init; }
    public required int RoutingPairs { get; init; }
    public required int OverlappingRoutingPairs { get; init; }
    public required decimal MeanRoutingOverlap { get; init; }
    public required decimal MaximumRoutingOverlap { get; init; }
    public required TextCost SkillTriggers { get; init; }
    public required TextCost SkillFiles { get; init; }
    public required TextCost LazyReferences { get; init; }
    public required TextCost MaximumPossibleLoad { get; init; }
    public required TextCost AlwaysVisible { get; init; }
    public required TextCost ActivationVisible { get; init; }
    public required TextCost AgentConfiguration { get; init; }
    public required TextCost AgentInstructions { get; init; }
}

public sealed record StaticCostReport
{
    public const string CurrentSchemaVersion = "1.0";
    public required string SchemaVersion { get; init; }
    public required string Subject { get; init; }
    public required string Revision { get; init; }
    public required string Tokenizer { get; init; }
    public required IReadOnlyList<SkillCost> Skills { get; init; }
    public required IReadOnlyList<AgentCost> Agents { get; init; }
    public required IReadOnlyList<RoutingOverlap> RoutingOverlap { get; init; }
    public required StaticCostSummary Summary { get; init; }
}

public static partial class StaticCostAnalyzer
{
    public const string TokenMethod = "OpenAI GPT-family UTF-8 byte approximation (ceil(bytes/4)); no model tokenizer available";

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "for", "from", "in", "of", "on", "or", "the", "to", "use", "when", "with"
    };

    public static StaticCostReport Analyze(string toolkitRoot, string? revision = null)
    {
        var root = Path.GetFullPath(toolkitRoot);
        var skillsRoot = Path.Combine(root, "plugins", "codex-toolkit", "skills");
        var agentsRoot = Path.Combine(root, "agents");
        if (!Directory.Exists(skillsRoot)) throw new InvalidOperationException("Toolkit skills directory was not found.");
        if (!Directory.Exists(agentsRoot)) throw new InvalidOperationException("Toolkit agents directory was not found.");

        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        var skills = Directory.GetDirectories(skillsRoot)
            .Order(StringComparer.Ordinal)
            .Select(path => AnalyzeSkill(path, descriptions))
            .ToArray();
        var agents = Directory.GetFiles(agentsRoot, "*.toml")
            .Order(StringComparer.Ordinal)
            .Select(AnalyzeAgent)
            .ToArray();
        var overlaps = AnalyzeOverlap(descriptions);

        return new StaticCostReport
        {
            SchemaVersion = StaticCostReport.CurrentSchemaVersion,
            Subject = "simplexidev/codex-toolkit",
            Revision = revision ?? ReadRevision(root),
            Tokenizer = TokenMethod,
            Skills = skills,
            Agents = agents,
            RoutingOverlap = overlaps,
            Summary = new StaticCostSummary
            {
                SkillCount = skills.Length,
                AgentCount = agents.Length,
                RoutingPairs = overlaps.Count,
                OverlappingRoutingPairs = overlaps.Count(item => item.SharedTerms > 0),
                MeanRoutingOverlap = overlaps.Count == 0 ? 0 : overlaps.Average(item => item.Jaccard),
                MaximumRoutingOverlap = overlaps.Count == 0 ? 0 : overlaps.Max(item => item.Jaccard),
                SkillTriggers = Sum(skills.Select(item => item.Trigger)),
                SkillFiles = Sum(skills.Select(item => item.SkillFile)),
                LazyReferences = Sum(skills.Select(item => item.LazyReferences)),
                MaximumPossibleLoad = Sum(skills.Select(item => item.MaximumPossibleLoad)),
                AlwaysVisible = Sum(skills.Select(item => item.AlwaysVisible)),
                ActivationVisible = Sum(skills.Select(item => item.ActivationVisible)),
                AgentConfiguration = Sum(agents.Select(item => item.Configuration)),
                AgentInstructions = Sum(agents.Select(item => item.Instructions))
            }
        };
    }

    public static string PublicAggregate(StaticCostReport report, DateTimeOffset generatedAt)
    {
        var summary = report.Summary;
        var metrics = new[]
        {
            Public("skill-count", summary.SkillCount, "count", "neutral", "measured", "filesystem inventory"),
            Public("custom-agent-count", summary.AgentCount, "count", "neutral", "measured", "filesystem inventory"),
            Public("skill-trigger-tokens", summary.SkillTriggers.Tokens, "estimated-tokens", "lower-is-better", "estimated", TokenMethod),
            Public("always-visible-tokens", summary.AlwaysVisible.Tokens, "estimated-tokens", "lower-is-better", "estimated", TokenMethod),
            Public("activation-visible-tokens", summary.ActivationVisible.Tokens, "estimated-tokens", "lower-is-better", "estimated", TokenMethod),
            Public("lazy-reference-tokens", summary.LazyReferences.Tokens, "estimated-tokens", "lower-is-better", "estimated", TokenMethod),
            Public("maximum-possible-load-tokens", summary.MaximumPossibleLoad.Tokens, "estimated-tokens", "lower-is-better", "estimated", TokenMethod),
            Public("agent-configuration-tokens", summary.AgentConfiguration.Tokens, "estimated-tokens", "lower-is-better", "estimated", TokenMethod),
            Public("agent-instruction-tokens", summary.AgentInstructions.Tokens, "estimated-tokens", "lower-is-better", "estimated", TokenMethod),
            Public("mean-routing-overlap", summary.MeanRoutingOverlap, "ratio", "lower-is-better", "derived", "pairwise Jaccard overlap of normalized skill trigger terms"),
            Public("maximum-routing-overlap", summary.MaximumRoutingOverlap, "ratio", "lower-is-better", "derived", "maximum pairwise Jaccard overlap of normalized skill trigger terms")
        };
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            generatedAt,
            subject = new { repository = report.Subject, revision = report.Revision },
            scenarios = new { total = 0, passed = 0 },
            metrics,
            provenance = new { sourceRuns = 1, sanitized = true, approval = "reviewed" }
        }, EvaluationRecordJson.Options);
    }

    public static TextCost Cost(string text)
    {
        var bytes = Encoding.UTF8.GetByteCount(text);
        return new TextCost { Bytes = bytes, Characters = text.Length, Tokens = (bytes + 3L) / 4L };
    }

    public static SkillCost AnalyzeSkill(string directory)
    {
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        return AnalyzeSkill(directory, descriptions);
    }

    private static SkillCost AnalyzeSkill(string directory, IDictionary<string, string> descriptions)
    {
        var path = Path.Combine(directory, "SKILL.md");
        if (!File.Exists(path)) throw new InvalidOperationException($"Missing SKILL.md in {Path.GetFileName(directory)}.");
        var text = File.ReadAllText(path);
        var match = FrontmatterRegex().Match(text);
        if (!match.Success) throw new InvalidOperationException($"Invalid SKILL.md frontmatter in {Path.GetFileName(directory)}.");
        var frontmatter = match.Groups[1].Value;
        var name = Field(frontmatter, "name") ?? Path.GetFileName(directory);
        var description = Field(frontmatter, "description") ?? "";
        descriptions[name] = description;
        var references = Directory.Exists(Path.Combine(directory, "references"))
            ? Directory.GetFiles(Path.Combine(directory, "references"), "*.md", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal).Select(File.ReadAllText).ToArray()
            : [];
        var lazy = string.Concat(references);
        var always = name + ": " + description;
        return new SkillCost
        {
            Name = name,
            Trigger = Cost(frontmatter),
            SkillFile = Cost(text),
            LazyReferences = Cost(lazy),
            MaximumPossibleLoad = Cost(text + lazy),
            AlwaysVisible = Cost(always),
            ActivationVisible = Cost(text),
            LazyReferenceFiles = references.Length
        };
    }

    public static AgentCost AnalyzeAgent(string path)
    {
        var text = File.ReadAllText(path);
        var match = InstructionsRegex().Match(text);
        var instructions = match.Success ? match.Groups[1].Value : "";
        var configuration = match.Success ? text.Remove(match.Index, match.Length) : text;
        var name = QuotedFieldRegex("name").Match(text).Groups[1].Value;
        return new AgentCost
        {
            Name = string.IsNullOrEmpty(name) ? Path.GetFileNameWithoutExtension(path) : name,
            Configuration = Cost(configuration),
            Instructions = Cost(instructions),
            Total = Cost(text)
        };
    }

    private static IReadOnlyList<RoutingOverlap> AnalyzeOverlap(IReadOnlyDictionary<string, string> descriptions)
    {
        var items = descriptions.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        var result = new List<RoutingOverlap>();
        for (var left = 0; left < items.Length; left++)
            for (var right = left + 1; right < items.Length; right++)
            {
                var leftTerms = Terms(items[left].Value);
                var rightTerms = Terms(items[right].Value);
                var shared = leftTerms.Intersect(rightTerms, StringComparer.Ordinal).Count();
                var union = leftTerms.Union(rightTerms, StringComparer.Ordinal).Count();
                result.Add(new RoutingOverlap
                {
                    Left = items[left].Key,
                    Right = items[right].Key,
                    SharedTerms = shared,
                    Jaccard = union == 0 ? 0 : decimal.Round((decimal)shared / union, 6)
                });
            }
        return result;
    }

    private static HashSet<string> Terms(string value) => WordRegex().Matches(value.ToLowerInvariant())
        .Select(match => match.Value)
        .Where(word => word.Length > 2 && !StopWords.Contains(word))
        .ToHashSet(StringComparer.Ordinal);

    private static TextCost Sum(IEnumerable<TextCost> costs)
    {
        var array = costs.ToArray();
        return new TextCost
        {
            Bytes = array.Sum(item => item.Bytes),
            Characters = array.Sum(item => item.Characters),
            Tokens = array.Sum(item => item.Tokens)
        };
    }

    private static object Public(string name, decimal value, string unit, string direction, string kind, string method) =>
        new { name, value, unit, direction, kind, method };

    private static string? Field(string frontmatter, string name)
    {
        var match = Regex.Match(frontmatter, $@"(?m)^{Regex.Escape(name)}:\s*(.+?)\s*$", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value.Trim().Trim('"', '\'') : null;
    }

    private static string ReadRevision(string root)
    {
        try
        {
            var start = new ProcessStartInfo("git", "rev-parse HEAD")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var process = Process.Start(start);
            var revision = process?.StandardOutput.ReadToEnd().Trim();
            process?.WaitForExit();
            if (process?.ExitCode == 0 && revision?.Length is >= 7 and <= 64 && revision.All(Uri.IsHexDigit)) return revision;
        }
        catch (System.ComponentModel.Win32Exception) { }
        return "0000000";
    }

    [GeneratedRegex(@"\A---\r?\n(.*?)\r?\n---\r?\n", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex FrontmatterRegex();

    [GeneratedRegex("(?ms)^developer_instructions\\s*=\\s*\\\"\\\"\\\"\\r?\\n?(.*?)\\\"\\\"\\\"\\s*(?:\\r?\\n|$)", RegexOptions.CultureInvariant)]
    private static partial Regex InstructionsRegex();

    [GeneratedRegex("[a-z0-9]+(?:[-'][a-z0-9]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    private static Regex QuotedFieldRegex(string name) => new($"(?m)^{Regex.Escape(name)}\\s*=\\s*\"([^\"]+)\"\\s*$", RegexOptions.CultureInvariant);
}
