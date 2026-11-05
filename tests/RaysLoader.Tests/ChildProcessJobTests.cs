using System.Diagnostics;
using RaysLoader.ProcessManagement;

namespace RaysLoader.Tests;

public sealed class ChildProcessJobTests
{
    [Fact]
    public async Task LaunchedToolsJoinTheKillOnCloseJob()
    {
        if (!OperatingSystem.IsWindows()) return;
        var ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new ProcessRunnerService();

        var run = runner.RunAsync(
            new ProcessRequest(ping, ["-n", "30", "127.0.0.1"]),
            _ => ValueTask.CompletedTask,
            started.SetResult,
            cancellation.Token);

        var processId = await started.Task.WaitAsync(cancellation.Token);
        bool assigned;
        using (var process = Process.GetProcessById(processId))
        {
            assigned = ChildProcessJob.IsAssigned(process);
        }

        await cancellation.CancelAsync();
        var result = await run;

        Assert.True(assigned);
        Assert.True(result.WasCancelled);
    }
}
