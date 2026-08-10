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
            new ToolLocatorService([], () => string.Empty),
            new BlockingUpdateService(blockCheck: false, blockDownload: false));

        await viewModel.ResetForSetupAsync();

        Assert.False(settings.Current.HasCompletedOnboarding);
        Assert.Equal(1, settings.ResetCount);
        Assert.Contains("setup walkthrough", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static SettingsViewModel CreateViewModel(IUpdateService updates) =>
        new(new StubSettingsService(), new ToolLocatorService([], () => string.Empty), updates);

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
}
