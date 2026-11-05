using RaysLoader.Models;
using RaysLoader.Services;
using RaysLoader.Settings;
using RaysLoader.ViewModels;

namespace RaysLoader.Tests;

public sealed class DevelopmentSettingsViewModelTests
{
    [Fact]
    public async Task DevelopmentModeCannotCheckDownloadOrLaunchPublicApplicationUpdates()
    {
        var updates = new RecordingUpdateService();
        using var viewModel = CreateViewModel(updates: updates);

        Assert.True(viewModel.IsDevelopmentMode);
        Assert.False(viewModel.ApplicationUpdatesEnabled);
        Assert.False(viewModel.HasAvailableUpdate);
        Assert.False(viewModel.CanDownloadUpdate);
        Assert.False(viewModel.CheckForUpdatesCommand.CanExecute(null));
        Assert.False(viewModel.DownloadAndInstallCommand.CanExecute(null));
        Assert.Contains("local development builds", viewModel.UpdateStatus);

        // Protect the command body too, including a stale availability binding.
        viewModel.HasAvailableUpdate = true;
        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);
        await viewModel.DownloadAndInstallCommand.ExecuteAsync(null);

        Assert.Equal(0, updates.CheckCount);
        Assert.Equal(0, updates.DownloadCount);
        Assert.Equal(0, updates.LaunchCount);
        Assert.False(viewModel.InstallerLaunched);
        Assert.False(viewModel.IsUpdateBusy);
        Assert.False(viewModel.CanDownloadUpdate);
    }

    [Fact]
    public void DefaultModeRetainsApplicationUpdater()
    {
        using var viewModel = new SettingsViewModel(new StubSettingsService(), new RecordingUpdateService(), new RecordingToolService());

        Assert.False(viewModel.IsDevelopmentMode);
        Assert.True(viewModel.ApplicationUpdatesEnabled);
        Assert.True(viewModel.HasAvailableUpdate);
        Assert.True(viewModel.CanDownloadUpdate);
        Assert.True(viewModel.CheckForUpdatesCommand.CanExecute(null));
        Assert.True(viewModel.DownloadAndInstallCommand.CanExecute(null));
        Assert.Equal("Version 1.1.0 is available.", viewModel.UpdateStatus);
    }

    [Fact]
    public async Task DetailsRefreshDoesNotClaimAnUpdateCheck()
    {
        var tools = new RecordingToolService(block: true);
        using var viewModel = CreateViewModel(tools);

        Assert.Contains("Update availability has not been checked", viewModel.ToolStatus);
        Assert.Equal("Check and update yt-dlp", viewModel.ToolUpdateActionText);
        var operation = viewModel.CheckToolStatusCommand.ExecuteAsync(null);
        await tools.OperationStarted.Task;
        Assert.Equal("Refreshing…", viewModel.ToolDetailsActionText);
        Assert.Equal("Check and update yt-dlp", viewModel.ToolUpdateActionText);
        Assert.True(viewModel.IsToolDetailsBusy);
        Assert.False(viewModel.IsYtDlpUpdateBusy);
        Assert.False(viewModel.UpdateYtDlpCommand.CanExecute(null));
        tools.ReleaseOperation.TrySetResult();
        await operation;

        Assert.Equal(1, tools.DetailsCount);
        Assert.Equal(0, tools.UpdateCount);
        Assert.Contains("does not check for yt-dlp updates", viewModel.ToolStatus);
        Assert.Equal("Refresh tool details", viewModel.ToolDetailsActionText);
        Assert.False(viewModel.IsToolDetailsBusy);
    }

    [Theory]
    [InlineData(false, "yt-dlp 2026.08.11.232958 is current on the nightly channel.")]
    [InlineData(true, "yt-dlp updated from 2026.08.01 to 2026.08.11.232958.")]
    [InlineData(false, "The selected custom yt-dlp is externally managed. Ray's Loader checked its version but did not replace it.")]
    public async Task CheckAndUpdateReportsActualOutcome(bool wasUpdated, string message)
    {
        var tools = new RecordingToolService(block: true, wasUpdated: wasUpdated, updateMessage: message);
        using var viewModel = CreateViewModel(tools);
        viewModel.Settings.YtDlpUpdateChannel = YtDlpUpdateChannel.Nightly;

        var operation = viewModel.UpdateYtDlpCommand.ExecuteAsync(null);
        await tools.OperationStarted.Task;
        Assert.True(viewModel.IsYtDlpUpdateBusy);
        Assert.False(viewModel.IsToolDetailsBusy);
        Assert.Equal("Checking and updating…", viewModel.ToolUpdateActionText);
        Assert.Contains("installing a newer yt-dlp version if available", viewModel.ToolStatus);
        Assert.False(viewModel.CheckToolStatusCommand.CanExecute(null));
        Assert.False(viewModel.UpdateYtDlpCommand.CanExecute(null));
        Assert.True(viewModel.CancelToolOperationCommand.CanExecute(null));
        tools.ReleaseOperation.TrySetResult();
        await operation;

        Assert.Equal(1, tools.UpdateCount);
        Assert.Equal(YtDlpUpdateChannel.Nightly, tools.LastUpdateChannel);
        Assert.Equal(message, viewModel.ToolStatus);
        Assert.Equal("yt-dlp: 2026.08.11.232958", viewModel.YtDlpVersionText);
        Assert.False(viewModel.IsToolBusy);
        Assert.False(viewModel.IsYtDlpUpdateBusy);
        Assert.Equal("Check and update yt-dlp", viewModel.ToolUpdateActionText);
        Assert.True(viewModel.UpdateYtDlpCommand.CanExecute(null));
        Assert.False(viewModel.CancelToolOperationCommand.CanExecute(null));
    }

    [Fact]
    public async Task CancelledUpdateDoesNotReuseStaleToolStatus()
    {
        var tools = new RecordingToolService(block: true);
        using var viewModel = CreateViewModel(tools);

        var operation = viewModel.UpdateYtDlpCommand.ExecuteAsync(null);
        await tools.OperationStarted.Task;
        viewModel.CancelToolOperationCommand.Execute(null);
        await operation;

        Assert.Contains("cancelled", viewModel.ToolStatus);
        Assert.NotEqual(tools.LastMessage, viewModel.ToolStatus);
        Assert.False(viewModel.IsToolBusy);
        Assert.Equal("Check and update yt-dlp", viewModel.ToolUpdateActionText);
    }

    [Fact]
    public async Task FailedUpdateDoesNotReuseStaleToolStatus()
    {
        var tools = new RecordingToolService(updateException: new IOException("Network connection failed."));
        using var viewModel = CreateViewModel(tools);
        await viewModel.CheckToolStatusCommand.ExecuteAsync(null);

        await viewModel.UpdateYtDlpCommand.ExecuteAsync(null);

        Assert.Equal("yt-dlp could not be checked or updated: Network connection failed.", viewModel.ToolStatus);
        Assert.NotEqual(tools.LastMessage, viewModel.ToolStatus);
        Assert.False(viewModel.IsToolBusy);
        Assert.True(viewModel.UpdateYtDlpCommand.CanExecute(null));
    }

    [Fact]
    public async Task ResetForSetupRefreshesAuthenticationChoices()
    {
        using var viewModel = CreateViewModel();
        viewModel.SelectedAuthenticationMethod = AuthenticationOptions.FindMethod(AuthenticationMode.Browser);
        viewModel.SelectedBrowser = AuthenticationOptions.FindBrowser("edge");

        await viewModel.ResetForSetupAsync();

        Assert.Equal(AuthenticationMode.None, viewModel.Settings.AuthenticationMode);
        Assert.Equal(AuthenticationMode.None, viewModel.SelectedAuthenticationMethod.Mode);
        Assert.False(viewModel.UsesBrowserAuthentication);
        Assert.Equal(AuthenticationOptions.FindBrowser(viewModel.Settings.BrowserCookies), viewModel.SelectedBrowser);
        Assert.False(viewModel.Settings.HasCompletedOnboarding);
    }

    private static SettingsViewModel CreateViewModel(IToolMaintenanceService? tools = null, IUpdateService? updates = null) =>
        new(new StubSettingsService(), updates ?? new RecordingUpdateService(), tools ?? new RecordingToolService(), developmentMode: true);

    private sealed class StubSettingsService : ISettingsService
    {
        public AppSettings Current { get; private set; } = new();
        public string SettingsDirectory => Path.GetTempPath();
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            Current = new AppSettings();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUpdateService : IUpdateService
    {
        public ReleaseVersion CurrentVersion { get; } = new(1, 0, 0);
        public AppUpdate? AvailableUpdate { get; } = new(
            new ReleaseVersion(1, 1, 0), "v1.1.0", "Ray's Loader 1.1.0",
            new Uri("https://example.com/releases/v1.1.0"), new Uri("https://example.com/installer.exe"),
            "RaysLoader-Setup-1.1.0.exe", 1, new string('A', 64), string.Empty);
        public UpdateCleanupReport LastCleanupReport => UpdateCleanupReport.Empty;
        public int CheckCount { get; private set; }
        public int DownloadCount { get; private set; }
        public int LaunchCount { get; private set; }

        public Task<AppUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            CheckCount++;
            return Task.FromResult(AvailableUpdate);
        }

        public Task<string> DownloadInstallerAsync(AppUpdate update, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            DownloadCount++;
            return Task.FromResult(Path.Combine(Path.GetTempPath(), update.InstallerName));
        }

        public Task LaunchInstallerAsync(AppUpdate update, string installerPath, CancellationToken cancellationToken = default)
        {
            LaunchCount++;
            return Task.CompletedTask;
        }

        public UpdateCleanupReport CleanupManagedArtifacts() => UpdateCleanupReport.Empty;
        public void CancelActiveOperations() { }
    }

    private sealed class RecordingToolService(
        bool block = false,
        bool wasUpdated = false,
        string? updateMessage = null,
        Exception? updateException = null) : IToolMaintenanceService
    {
        public TaskCompletionSource OperationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseOperation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ToolHealthSnapshot? LastSnapshot { get; private set; }
        public string LastMessage { get; private set; } = "Backend status has not been checked yet.";
        public int DetailsCount { get; private set; }
        public int UpdateCount { get; private set; }
        public YtDlpUpdateChannel? LastUpdateChannel { get; private set; }

        public async Task<ToolHealthSnapshot> CheckStatusAsync(CancellationToken cancellationToken = default)
        {
            DetailsCount++;
            await WaitAsync(cancellationToken);
            LastMessage = "The download engine is ready.";
            return LastSnapshot = CreateSnapshot();
        }

        public async Task<YtDlpMaintenanceResult> CheckAndUpdateYtDlpAsync(
            YtDlpUpdateChannel channel,
            CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            LastUpdateChannel = channel;
            if (updateException is not null) throw updateException;
            await WaitAsync(cancellationToken);
            LastSnapshot = CreateSnapshot();
            return new YtDlpMaintenanceResult(LastSnapshot, wasUpdated, updateMessage ?? $"yt-dlp {LastSnapshot.YtDlpVersion} is current.");
        }

        private async Task WaitAsync(CancellationToken cancellationToken)
        {
            OperationStarted.TrySetResult();
            if (block) await ReleaseOperation.Task.WaitAsync(cancellationToken);
        }

        public Task WaitForIdleAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        private static ToolHealthSnapshot CreateSnapshot() => new(
            "2026.08.11.232958", "C:\\tools\\yt-dlp.exe", "Managed by Ray's Loader",
            "8.0", "8.0", "node 22.14.0", true, true, true, true);
    }
}
