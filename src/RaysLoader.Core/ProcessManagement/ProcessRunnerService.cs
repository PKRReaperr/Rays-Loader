using System.Diagnostics;

namespace RaysLoader.ProcessManagement;

public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string?>? Environment = null);

public sealed record ProcessOutputLine(string Text, bool IsError, DateTimeOffset Timestamp);
public sealed record ProcessRunResult(int ExitCode, bool WasCancelled);

public interface IProcessRunnerService
{
    Task<ProcessRunResult> RunAsync(
        ProcessRequest request,
        Func<ProcessOutputLine, ValueTask> onOutput,
        Action<int>? onStarted = null,
        CancellationToken cancellationToken = default);
}

public sealed class ProcessRunnerService : IProcessRunnerService
{
    public async Task<ProcessRunResult> RunAsync(
        ProcessRequest request,
        Func<ProcessOutputLine, ValueTask> onOutput,
        Action<int>? onStarted = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(onOutput);

        using var process = new Process
        {
            StartInfo = CreateStartInfo(request),
            EnableRaisingEvents = true,
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Could not start '{request.FileName}'.");
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ProcessLaunchException($"Ray's Loader could not start '{request.FileName}'.", exception);
        }

        onStarted?.Invoke(process.Id);
        // Keep draining redirected pipes while cancellation tears down the process.
        // Cancelling these readers first can lose the final diagnostic lines or
        // surface a reader cancellation instead of the intended process result.
        var stdoutTask = ReadStreamAsync(process.StandardOutput, false, onOutput, CancellationToken.None);
        var stderrTask = ReadStreamAsync(process.StandardError, true, onOutput, CancellationToken.None);
        var cancelled = false;

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            TryKillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        return new ProcessRunResult(process.ExitCode, cancelled);
    }

    private static ProcessStartInfo CreateStartInfo(ProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.Environment is not null)
        {
            foreach (var item in request.Environment)
            {
                startInfo.Environment[item.Key] = item.Value;
            }
        }

        return startInfo;
    }

    private static async Task ReadStreamAsync(
        StreamReader reader,
        bool isError,
        Func<ProcessOutputLine, ValueTask> onOutput,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            await onOutput(new ProcessOutputLine(line, isError, DateTimeOffset.Now)).ConfigureAwait(false);
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process exited between the checks.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The process is already inaccessible; WaitForExit will settle the final state.
        }
    }
}

public sealed class ProcessLaunchException(string message, Exception innerException) : Exception(message, innerException);
