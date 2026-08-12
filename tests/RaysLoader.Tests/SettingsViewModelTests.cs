using RaysLoader.Services;
using RaysLoader.Settings;
using RaysLoader.ViewModels;

namespace RaysLoader.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task CancelCommandCancelsActiveMetadataCheckAndPreventsOverlap()
    {
        var updates = new BlockingUpdateService(blockCheck: true, blockDownload: false);
        using var viewModel = CreateViewModel(updates);

        var operation = viewModel.CheckForUpdatesCommand.ExecuteAsync(null);
        await updates.OperationStarted.Task;

        Assert.True(viewModel.IsUpdateBusy);
        Assert.True(viewModel.CancelUpdateCommand.CanExecute(null));
        Assert.False(viewModel.CheckForUpdatesCommand.CanExecute(null));
        viewModel.CancelUpdateCommand.Execute(null);
        await operation;

        Assert.False(viewModel.IsUpdateBusy);
        Assert.Equal("Update check cancelled.", viewModel.UpdateStatus);
    }

    [Fact]
    public async Task DisposeCancelsActiveInstallerDownload()
    {
        var updates = new BlockingUpdateService(blockCheck: false, blockDownload: true);
        var viewModel = CreateViewModel(updates);

        var operation = viewModel.DownloadAndInstallCommand.ExecuteAsync(null);
        await updates.OperationStarted.Task;
        viewModel.Dispose();
        await operation;

        Assert.False(viewModel.InstallerLaunched);
        Assert.Equal("Update cancelled. The current installation was not changed.", viewModel.UpdateStatus);
    }

    [Fact]
    public async Task ResetForSetupRestoresFirstRunWalkthrough()
    {
        var settings = new StubSettingsService();
        settings.Current.HasCompletedOnboarding = true;
        using var viewModel = new SettingsViewModel(
            settings,
            new BlockingUpdateService(blockCheck: false, blockDownload: false),
            new StubToolMaintenanceService());

        await viewModel.ResetForSetupAsync();

        Assert.False(settings.Current.HasCompletedOnboarding);
        Assert.Equal(1, settings.ResetCount);
        Assert.Contains("setup walkthrough", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ToolStatusCheckDisplaysBackendVersions()
    {
        var tools = new StubToolMaintenanceService();
        using var viewModel = CreateViewModel(
            new BlockingUpdateService(blockCheck: false, blockDownload: false),
            tools);

        await viewModel.CheckToolStatusCommand.ExecuteAsync(null);

        Assert.Equal("yt-dlp: 2026.08.11.232958", viewModel.YtDlpVersionText);
        Assert.Contains("FFmpeg: 8.0", viewModel.FfmpegVersionText);
        Assert.Contains("node 22.14.0", viewModel.JavaScriptRuntimeText);
        Assert.Equal("The download engine is ready.", viewModel.ToolStatus);
    }

    [Fact]
    public async Task CancelToolCommandCancelsActiveBackendCheck()
    {
        var tools = new StubToolMaintenanceService(block: true);
        using var viewModel = CreateViewModel(
            new BlockingUpdateService(blockCheck: false, blockDownload: false),
            tools);

        var operation = viewModel.CheckToolStatusCommand.ExecuteAsync(null);
        await tools.OperationStarted.Task;
        Assert.True(viewModel.IsToolBusy);
        viewModel.CancelToolOperationCommand.Execute(null);
        await operation;

        Assert.False(viewModel.IsToolBusy);
        Assert.Equal("Backend status check cancelled.", viewModel.ToolStatus);
    }

    private static SettingsViewModel CreateViewModel(IUpdateService updates, IToolMaintenanceService? tools = null) =>
        new(new StubSettingsService(), updates, tools ?? new StubToolMaintenanceService());

    private sealed class StubSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public int ResetCount { get; private set; }
        public string SettingsDirectory => Path.GetTempPath();
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            ResetCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingUpdateService(bool blockCheck, bool blockDownload) : IUpdateService
    {
        private static readonly ReleaseVersion UpdateVersion = new(1, 1, 0);
        public TaskCompletionSource OperationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ReleaseVersion CurrentVersion { get; } = new(1, 0, 0);
        public AppUpdate? AvailableUpdate { get; } = CreateUpdate();
        public UpdateCleanupReport LastCleanupReport { get; } = UpdateCleanupReport.Empty;

        public async Task<AppUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            OperationStarted.TrySetResult();
            if (blockCheck) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return AvailableUpdate;
        }

        public async Task<string> DownloadInstallerAsync(AppUpdate update, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            OperationStarted.TrySetResult();
            if (blockDownload) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Path.Combine(Path.GetTempPath(), update.InstallerName);
        }

        public Task LaunchInstallerAsync(AppUpdate update, string installerPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public UpdateCleanupReport CleanupManagedArtifacts() => UpdateCleanupReport.Empty;
        public void CancelActiveOperations() { }

        private static AppUpdate CreateUpdate() => new(
            UpdateVersion,
            "v1.1.0",
            "Ray's Loader 1.1.0",
            new Uri("https://github.com/PKRReaperr/Rays-Loader/releases/tag/v1.1.0"),
            new Uri("https://github.com/PKRReaperr/Rays-Loader/releases/download/v1.1.0/RaysLoader-Setup-1.1.0.exe"),
            UpdateVersion.InstallerFileName,
            1,
            new string('A', 64),
            string.Empty);
    }

    private sealed class StubToolMaintenanceService(bool block = false) : IToolMaintenanceService
    {
        public TaskCompletionSource OperationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ToolHealthSnapshot? LastSnapshot { get; private set; }
        public string LastMessage { get; private set; } = "Backend status has not been checked yet.";

        public async Task<ToolHealthSnapshot> CheckStatusAsync(CancellationToken cancellationToken = default)
        {
            OperationStarted.TrySetResult();
            if (block) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            LastSnapshot = CreateSnapshot();
            LastMessage = "The download engine is ready.";
            return LastSnapshot;
        }

        public async Task<YtDlpMaintenanceResult> CheckAndUpdateYtDlpAsync(
            YtDlpUpdateChannel channel,
            CancellationToken cancellationToken = default)
        {
            var snapshot = await CheckStatusAsync(cancellationToken);
            return new YtDlpMaintenanceResult(snapshot, false, $"yt-dlp {snapshot.YtDlpVersion} is current.");
        }

        public Task WaitForIdleAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        private static ToolHealthSnapshot CreateSnapshot() => new(
            "2026.08.11.232958",
            "C:\\tools\\yt-dlp.exe",
            "Managed by Ray's Loader",
            "8.0",
            "8.0",
            "node 22.14.0",
            true,
            true,
            true,
            true);
    }
}
