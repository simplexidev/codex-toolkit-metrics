namespace SdevEng.Metrics.Tests;

public sealed class CodexCliExecutorTests
{
    [Fact]
    public async Task TerminatesProcessTreeAtBoundedTimeout()
    {
        if (OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), $"codex-executor-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var executable = Path.Combine(root, "slow-codex");
        await File.WriteAllTextAsync(executable, "#!/bin/sh\nsleep 30\n");
        File.SetUnixFileMode(executable,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            var result = await new CodexCliExecutor().ExecuteAsync(
                new ExecutionRequest(
                    "prompt",
                    root,
                    RunnerTestSupport.Plan("fixture").Executor with { Executable = executable },
                    TimeSpan.FromMilliseconds(100)),
                CancellationToken.None);

            Assert.True(result.TimedOut);
            Assert.Null(result.ExitCode);
            Assert.Contains("Timed out", result.Failure, StringComparison.Ordinal);
            Assert.True(result.Elapsed < TimeSpan.FromSeconds(5));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
