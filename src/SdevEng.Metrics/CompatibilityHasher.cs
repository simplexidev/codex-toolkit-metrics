using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SdevEng.Metrics;

public static class CompatibilityHasher
{
    public const string Version = "runner-v4";

    public static async Task<string> ComputeAsync(
        EvaluationPlan plan,
        EvaluationScenario scenario,
        EvaluationArmDefinition arm,
        string planDirectory,
        CancellationToken cancellationToken = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, Version);
        Add(hash, JsonSerializer.Serialize(new
        {
            scenario,
            arm,
            plan.Executor.Provider,
            plan.Executor.Model,
            plan.Executor.Reasoning,
            plan.Executor.Version,
            plan.Executor.Executable,
            plan.TimeoutSeconds,
            plan.Judge
        }, EvaluationRecordJson.Options));

        await AddPathAsync(hash, Resolve(planDirectory, plan.FixtureRoot), cancellationToken);
        foreach (var path in arm.SkillPaths.Concat(arm.AgentPaths).Order(StringComparer.Ordinal))
        {
            await AddPathAsync(hash, Resolve(planDirectory, path), cancellationToken);
        }

        if (arm.InstructionFile is not null)
        {
            await AddPathAsync(hash, Resolve(planDirectory, arm.InstructionFile), cancellationToken);
        }

        if (scenario.PromptFile is not null)
        {
            await AddPathAsync(hash, Resolve(planDirectory, scenario.PromptFile), cancellationToken);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static string Resolve(string baseDirectory, string path) =>
        Path.GetFullPath(path, baseDirectory);

    private static async Task AddPathAsync(
        IncrementalHash hash,
        string path,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            Add(hash, Path.GetFileName(path));
            hash.AppendData(await File.ReadAllBytesAsync(path, cancellationToken));
            return;
        }

        if (!Directory.Exists(path))
        {
            throw new FileNotFoundException("A behavior-affecting input does not exist.", path);
        }

        foreach (var entry in EnumerateEntries(path))
        {
            var isDirectory = Directory.Exists(entry);
            var relative = Path.GetRelativePath(path, entry).Replace(Path.DirectorySeparatorChar, '/') +
                (isDirectory ? "/" : "");
            Add(hash, relative);
            if (!OperatingSystem.IsWindows()) Add(hash, File.GetUnixFileMode(entry).ToString());
            if (!isDirectory)
            {
                hash.AppendData(await File.ReadAllBytesAsync(entry, cancellationToken));
            }
        }
    }

    internal static IEnumerable<string> EnumerateEntries(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Behavior inputs may not contain symbolic links: {entry}");
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                yield return entry;
                foreach (var child in EnumerateEntries(entry)) yield return child;
            }
            else yield return entry;
        }
    }

    internal static IEnumerable<string> EnumerateRegularFiles(string directory) =>
        EnumerateEntries(directory).Where(File.Exists);

    private static void Add(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }
}
