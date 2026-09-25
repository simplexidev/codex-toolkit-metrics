using System.Diagnostics;

namespace SdevEng.Metrics;

public sealed class CodexCliExecutor : IEvaluationExecutor
{
    public async Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        using var codexHome = EphemeralCodexHome.Create(
            Path.Combine(Environment.CurrentDirectory, "data", "private", ".executor-homes"));
        var info = new ProcessStartInfo
        {
            FileName = request.Provider.Executable,
            WorkingDirectory = request.Workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.Environment.Remove("TYPESAFE_API_KEY");
        info.Environment["CODEX_HOME"] = codexHome.Path;
        foreach (var argument in new[]
        {
            "exec", "--json", "--ephemeral", "--ignore-user-config", "--skip-git-repo-check",
            "--sandbox", request.Sandbox,
            "--model", request.Provider.Model,
            "-c", $"model_reasoning_effort=\"{request.Provider.Reasoning}\"",
            "-"
        })
        {
            info.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Codex CLI did not start.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Failure(exception.Message, start);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var timedOut = false;
        try
        {
            await process.StandardInput.WriteAsync(request.Prompt.AsMemory(), linked.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        cancellationToken.ThrowIfCancellationRequested();
        var parsed = CodexJsonlParser.Parse(stdout);
        var observations = parsed.Observations with
        {
            ContextIsolated = parsed.Observations.ContextIsolated ?? request.ContextIsolated
        };
        return new ExecutionResult
        {
            ExitCode = timedOut ? null : process.ExitCode,
            TimedOut = timedOut,
            Response = parsed.Response,
            StandardOutput = stdout,
            StandardError = stderr,
            Elapsed = Stopwatch.GetElapsedTime(start),
            Usage = parsed.Usage,
            Observations = observations,
            Failure = timedOut ? $"Timed out after {request.Timeout.TotalSeconds:0} seconds." :
                process.ExitCode == 0 ? null : "Codex CLI returned a non-zero exit code."
        };
    }

    private static ExecutionResult Failure(string message, long start) => new()
    {
        ExitCode = null,
        TimedOut = false,
        Response = "",
        StandardOutput = "",
        StandardError = "",
        Elapsed = Stopwatch.GetElapsedTime(start),
        Failure = message
    };

    private sealed class EphemeralCodexHome : IDisposable
    {
        private EphemeralCodexHome(string path) => Path = path;

        public string Path { get; }

        public static EphemeralCodexHome Create(string root)
        {
            Directory.CreateDirectory(root);
            var path = System.IO.Path.Combine(root, $"codex-metrics-home-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            try
            {
                var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
                var source = !string.IsNullOrWhiteSpace(configured)
                    ? configured
                    : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                CopyPrivateFile(source, path, "auth.json");
                CopyPrivateFile(source, path, "models_cache.json");
                return new EphemeralCodexHome(path);
            }
            catch
            {
                Directory.Delete(path, recursive: true);
                throw;
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }

        private static void CopyPrivateFile(string sourceDirectory, string destinationDirectory, string name)
        {
            var source = System.IO.Path.Combine(sourceDirectory, name);
            if (!File.Exists(source)) return;
            var destination = System.IO.Path.Combine(destinationDirectory, name);
            File.Copy(source, destination, overwrite: false);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
