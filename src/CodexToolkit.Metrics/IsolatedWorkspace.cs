namespace CodexToolkit.Metrics;

public sealed class IsolatedWorkspace : IDisposable
{
    private IsolatedWorkspace(string path) => Path = path;

    public string Path { get; }

    public static IsolatedWorkspace Create(
        string fixtureRoot,
        EvaluationArmDefinition arm,
        string planDirectory)
    {
        var workspaceRoot = System.IO.Path.Combine(
            Environment.CurrentDirectory, "data", "private", ".workspaces");
        Directory.CreateDirectory(workspaceRoot);
        var root = System.IO.Path.Combine(workspaceRoot, $"codex-toolkit-metrics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            CopyDirectory(CompatibilityHasher.Resolve(planDirectory, fixtureRoot), root);
            foreach (var skillPath in arm.SkillPaths)
            {
                CopyOverlay(
                    CompatibilityHasher.Resolve(planDirectory, skillPath),
                    System.IO.Path.Combine(root, ".codex", "skills"));
            }

            foreach (var agentPath in arm.AgentPaths)
            {
                CopyOverlay(
                    CompatibilityHasher.Resolve(planDirectory, agentPath),
                    System.IO.Path.Combine(root, ".codex", "agents"));
            }

            return new IsolatedWorkspace(root);
        }
        catch
        {
            Directory.Delete(root, recursive: true);
            throw;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }

    private static void CopyOverlay(string source, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        var destination = System.IO.Path.Combine(destinationRoot, System.IO.Path.GetFileName(source));
        if (Directory.Exists(source))
        {
            CopyDirectory(source, destination);
        }
        else if (File.Exists(source))
        {
            File.Copy(source, destination, overwrite: false);
        }
        else
        {
            throw new FileNotFoundException("Arm overlay does not exist.", source);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        Directory.CreateDirectory(destination);
        foreach (var entry in CompatibilityHasher.EnumerateEntries(source))
        {
            var target = System.IO.Path.Combine(destination, System.IO.Path.GetRelativePath(source, entry));
            if (Directory.Exists(entry))
            {
                Directory.CreateDirectory(target);
            }
            else
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                File.Copy(entry, target, overwrite: false);
            }
        }
    }
}
