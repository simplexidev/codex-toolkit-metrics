using System.Diagnostics;

namespace CodexToolkit.Metrics;

public sealed class CodexCliExecutor : IEvaluationExecutor
{
    public async Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
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
        return new ExecutionResult
        {
            ExitCode = timedOut ? null : process.ExitCode,
            TimedOut = timedOut,
            Response = parsed.Response,
            StandardOutput = stdout,
            StandardError = stderr,
            Elapsed = Stopwatch.GetElapsedTime(start),
            Usage = parsed.Usage,
            Observations = parsed.Observations,
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
}
