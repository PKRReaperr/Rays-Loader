using RaysLoader.ProcessManagement;
using RaysLoader.Services;
using RaysLoader.Settings;

namespace RaysLoader.Tests;

public sealed class ToolMaintenanceServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "RaysLoader.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StatusIncludesYtDlpFfmpegAndJavaScriptRuntimeVersions()
    {
        var fixture = CreateFixture();

        var status = await fixture.Service.CheckStatusAsync();

        Assert.Equal("2026.06.09", status.YtDlpVersion);
        Assert.Equal("Bundled with Ray's Loader", status.YtDlpSource);
        Assert.Equal("8.0", status.FfmpegVersion);
        Assert.Equal("8.0", status.FfprobeVersion);
        Assert.Equal("node 22.14.0", status.JavaScriptRuntime);
        Assert.True(status.HasJavaScriptRuntime);
    }

    [Fact]
    public async Task UpdateSeedsManagedCopyAndMakesItTheActiveBackend()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.CheckAndUpdateYtDlpAsync(YtDlpUpdateChannel.Nightly);

        Assert.True(result.WasUpdated);
        Assert.Equal("2026.08.11.232958", result.Snapshot.YtDlpVersion);
        Assert.Equal("Managed by Ray's Loader", result.Snapshot.YtDlpSource);
        Assert.True(File.Exists(Path.Combine(fixture.ManagedDirectory, "yt-dlp.exe")));
        Assert.Contains("nightly@latest", fixture.Runner.LastUpdateArguments);
    }

    [Fact]
    public async Task CustomYtDlpIsCheckedButNeverReplaced()
    {
        var fixture = CreateFixture(useCustomYtDlp: true);

        var result = await fixture.Service.CheckAndUpdateYtDlpAsync(YtDlpUpdateChannel.Nightly);

        Assert.False(result.WasUpdated);
        Assert.False(fixture.Runner.UpdateWasRun);
        Assert.Contains("externally managed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(fixture.ManagedDirectory, "yt-dlp.exe")));
    }

    [Fact]
    public async Task CancellationRestoresKnownGoodManagedExecutable()
    {
        var fixture = CreateFixture(blockUpdate: true);
        var original = File.ReadAllBytes(fixture.BundledYtDlp);
        using var cancellation = new CancellationTokenSource();

        var operation = fixture.Service.CheckAndUpdateYtDlpAsync(YtDlpUpdateChannel.Nightly, cancellation.Token);
        await fixture.Runner.UpdateStarted.Task;
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(fixture.ManagedDirectory, "yt-dlp.exe")));
        Assert.False(File.Exists(Path.Combine(fixture.ManagedDirectory, "yt-dlp.rollback.exe")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private Fixture CreateFixture(bool useCustomYtDlp = false, bool blockUpdate = false)
    {
        var bundledDirectory = Path.Combine(root, "app", "tools");
        var managedDirectory = Path.Combine(root, "settings", "tools");
        var bundledYtDlp = CreateExecutable(bundledDirectory, "yt-dlp.exe", [0x4D, 0x5A, 0x01]);
        var ffmpeg = CreateExecutable(bundledDirectory, "ffmpeg.exe", [0x4D, 0x5A, 0x02]);
        var ffprobe = CreateExecutable(bundledDirectory, "ffprobe.exe", [0x4D, 0x5A, 0x03]);
        var node = CreateExecutable(Path.Combine(root, "node"), "node.exe", [0x4D, 0x5A, 0x04]);
        var custom = useCustomYtDlp
            ? CreateExecutable(Path.Combine(root, "custom"), "custom-yt-dlp.exe", [0x4D, 0x5A, 0x05])
            : string.Empty;
        var settings = new StubSettingsService(Path.Combine(root, "settings"));
        settings.Current.YtDlpPath = custom;
        var locator = new DynamicToolLocator(bundledYtDlp, managedDirectory, ffmpeg, ffprobe, node, custom);
        var runner = new StubProcessRunner(blockUpdate);
        var service = new ToolMaintenanceService(runner, locator, settings, managedDirectory, bundledDirectory);
        return new Fixture(service, runner, managedDirectory, bundledYtDlp);
    }

    private static string CreateExecutable(string directory, string name, byte[] content)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, content);
        return Path.GetFullPath(path);
    }

    private sealed record Fixture(
        ToolMaintenanceService Service,
        StubProcessRunner Runner,
        string ManagedDirectory,
        string BundledYtDlp);

    private sealed class DynamicToolLocator(
        string bundledYtDlp,
        string managedDirectory,
        string ffmpeg,
        string ffprobe,
        string node,
        string customYtDlp) : IToolLocatorService
    {
        public ToolPaths Locate(AppSettings settings)
        {
            var managed = Path.Combine(managedDirectory, "yt-dlp.exe");
            var ytDlp = customYtDlp.Length > 0
                ? customYtDlp
                : File.Exists(managed) ? managed : bundledYtDlp;
            return new ToolPaths(ytDlp, ffmpeg, ffprobe, $"node:{node}");
        }
    }

    private sealed class StubProcessRunner(bool blockUpdate) : IProcessRunnerService
    {
        private string ytDlpVersion = "2026.06.09";
        public TaskCompletionSource UpdateStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool UpdateWasRun { get; private set; }
        public string LastUpdateArguments { get; private set; } = string.Empty;

        public async Task<ProcessRunResult> RunAsync(
            ProcessRequest request,
            Func<ProcessOutputLine, ValueTask> onOutput,
            Action<int>? onStarted = null,
            CancellationToken cancellationToken = default)
        {
            if (request.Arguments.Contains("--update-to"))
            {
                UpdateWasRun = true;
                LastUpdateArguments = string.Join(' ', request.Arguments);
                UpdateStarted.TrySetResult();
                File.WriteAllBytes(request.FileName, [0x4D, 0x5A, 0x99]);
                if (blockUpdate)
                {
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return new ProcessRunResult(-1, true);
                    }
                }
                ytDlpVersion = "2026.08.11.232958";
                await onOutput(new ProcessOutputLine("Updated yt-dlp", false, DateTimeOffset.Now));
                return new ProcessRunResult(0, false);
            }

            var fileName = Path.GetFileName(request.FileName);
            var output = fileName.Equals("yt-dlp.exe", StringComparison.OrdinalIgnoreCase) ||
                         fileName.Equals("custom-yt-dlp.exe", StringComparison.OrdinalIgnoreCase)
                ? ytDlpVersion
                : fileName.Equals("node.exe", StringComparison.OrdinalIgnoreCase)
                    ? "v22.14.0"
                    : fileName.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase)
                        ? "ffprobe version 8.0 Copyright"
                        : "ffmpeg version 8.0 Copyright";
            await onOutput(new ProcessOutputLine(output, fileName.StartsWith("ff", StringComparison.OrdinalIgnoreCase), DateTimeOffset.Now));
            return new ProcessRunResult(0, false);
        }
    }

    private sealed class StubSettingsService(string settingsDirectory) : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public string SettingsDirectory { get; } = settingsDirectory;
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
