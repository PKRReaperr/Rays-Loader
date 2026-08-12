using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RaysLoader.Models;
using RaysLoader.ProcessManagement;
using RaysLoader.Services;
using RaysLoader.Settings;

namespace RaysLoader.ViewModels;

public sealed partial class SettingsViewModel(
    ISettingsService settingsService,
    IUpdateService updateService,
    IToolMaintenanceService toolMaintenanceService) : ObservableObject, IDisposable
{
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string updateStatus = CreateInitialUpdateStatus(updateService);
    [ObservableProperty] private double updateProgress;
    [ObservableProperty] private bool isUpdateBusy;
    [ObservableProperty] private bool hasAvailableUpdate = updateService.AvailableUpdate is not null;
    [ObservableProperty] private AuthenticationMethodOption selectedAuthenticationMethod = AuthenticationOptions.FindMethod(settingsService.Current.AuthenticationMode);
    [ObservableProperty] private BrowserCookieOption selectedBrowser = AuthenticationOptions.FindBrowser(settingsService.Current.BrowserCookies);
    [ObservableProperty] private string ytDlpVersionText = FormatYtDlpVersion(toolMaintenanceService.LastSnapshot);
    [ObservableProperty] private string ytDlpSourceText = FormatYtDlpSource(toolMaintenanceService.LastSnapshot);
    [ObservableProperty] private string ffmpegVersionText = FormatFfmpegVersion(toolMaintenanceService.LastSnapshot);
    [ObservableProperty] private string javaScriptRuntimeText = FormatJavaScriptRuntime(toolMaintenanceService.LastSnapshot);
    [ObservableProperty] private string toolStatus = toolMaintenanceService.LastMessage;
    [ObservableProperty] private bool isToolBusy;
    private CancellationTokenSource? updateCancellation;
    private CancellationTokenSource? toolCancellation;

    public string CurrentVersionText => $"Current version: {updateService.CurrentVersion}";
    public bool CanDownloadUpdate => HasAvailableUpdate && !IsUpdateBusy;
    public bool InstallerLaunched { get; private set; }

    public AppSettings Settings => settingsService.Current;
    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();
    public IReadOnlyList<AppAccent> AccentColors { get; } = Enum.GetValues<AppAccent>();
    public IReadOnlyList<AuthenticationMethodOption> AuthenticationMethods { get; } = AuthenticationOptions.Methods;
    public IReadOnlyList<BrowserCookieOption> Browsers { get; } = AuthenticationOptions.Browsers;
    public IReadOnlyList<YtDlpUpdateChannel> YtDlpUpdateChannels { get; } = Enum.GetValues<YtDlpUpdateChannel>();
    public bool UsesBrowserAuthentication => SelectedAuthenticationMethod.Mode is AuthenticationMode.Browser;
    public bool UsesCookiesFile => SelectedAuthenticationMethod.Mode is AuthenticationMode.CookiesFile;

    partial void OnSelectedAuthenticationMethodChanged(AuthenticationMethodOption value)
    {
        Settings.AuthenticationMode = value.Mode;
        OnPropertyChanged(nameof(UsesBrowserAuthentication));
        OnPropertyChanged(nameof(UsesCookiesFile));
    }

    partial void OnSelectedBrowserChanged(BrowserCookieOption value) => Settings.BrowserCookies = value.Value;

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            await settingsService.SaveAsync(Settings);
            StatusMessage = "Settings saved.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            StatusMessage = $"Settings could not be saved: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartToolOperation))]
    private async Task CheckToolStatusAsync()
    {
        if (!TryBeginToolOperation(out var cancellation)) return;
        ToolStatus = "Checking the download engine...";
        try
        {
            ApplyToolSnapshot(await toolMaintenanceService.CheckStatusAsync(cancellation.Token));
            ToolStatus = toolMaintenanceService.LastMessage;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            ToolStatus = "Backend status check cancelled.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ProcessLaunchException)
        {
            ToolStatus = $"Backend status check failed: {exception.Message}";
        }
        finally
        {
            EndToolOperation(cancellation);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartToolOperation))]
    private async Task UpdateYtDlpAsync()
    {
        if (!TryBeginToolOperation(out var cancellation)) return;
        ToolStatus = $"Checking the {Settings.YtDlpUpdateChannel.ToString().ToLowerInvariant()} yt-dlp channel...";
        try
        {
            var result = await toolMaintenanceService.CheckAndUpdateYtDlpAsync(
                Settings.YtDlpUpdateChannel,
                cancellation.Token);
            ApplyToolSnapshot(result.Snapshot);
            ToolStatus = result.Message;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            ToolStatus = toolMaintenanceService.LastMessage;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or YtDlpException or MissingToolException or ProcessLaunchException)
        {
            ToolStatus = toolMaintenanceService.LastMessage.Length > 0
                ? toolMaintenanceService.LastMessage
                : $"yt-dlp update failed: {exception.Message}";
        }
        finally
        {
            EndToolOperation(cancellation);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelToolOperation))]
    private void CancelToolOperation()
    {
        ToolStatus = "Cancelling backend operation...";
        toolCancellation?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanStartUpdateOperation))]
    private async Task CheckForUpdatesAsync()
    {
        if (!TryBeginUpdateOperation(out var cancellation)) return;
        UpdateProgress = 0;
        UpdateStatus = "Checking GitHub for updates...";
        try
        {
            var update = await updateService.CheckForUpdateAsync(cancellation.Token);
            HasAvailableUpdate = update is not null;
            UpdateStatus = update is null
                ? "Ray's Loader is up to date."
                : $"Version {update.Version} is available.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            UpdateStatus = "Update check cancelled.";
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException or System.Text.Json.JsonException)
        {
            HasAvailableUpdate = false;
            UpdateStatus = $"Update check failed: {exception.Message}";
        }
        finally
        {
            EndUpdateOperation(cancellation);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartDownloadOperation))]
    private async Task DownloadAndInstallAsync()
    {
        var update = updateService.AvailableUpdate;
        if (update is null || !TryBeginUpdateOperation(out var cancellation)) return;

        InstallerLaunched = false;
        UpdateProgress = 0;
        UpdateStatus = $"Downloading version {update.Version}...";
        try
        {
            var progress = new Progress<double>(value => UpdateProgress = value);
            var installer = await updateService.DownloadInstallerAsync(update, progress, cancellation.Token);
            UpdateStatus = "Installer verified. Starting the upgrade...";
            await updateService.LaunchInstallerAsync(update, installer, cancellation.Token);
            InstallerLaunched = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            UpdateStatus = "Update cancelled. The current installation was not changed.";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or TaskCanceledException or System.ComponentModel.Win32Exception)
        {
            UpdateStatus = $"Update failed: {exception.Message}";
        }
        finally
        {
            EndUpdateOperation(cancellation);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelUpdate))]
    private void CancelUpdate()
    {
        UpdateStatus = "Cancelling update...";
        updateCancellation?.Cancel();
    }

    [RelayCommand]
    private async Task ResetAsync()
    {
        await settingsService.ResetAsync();
        OnPropertyChanged(nameof(Settings));
        SelectedAuthenticationMethod = AuthenticationOptions.FindMethod(Settings.AuthenticationMode);
        SelectedBrowser = AuthenticationOptions.FindBrowser(Settings.BrowserCookies);
        StatusMessage = "Settings reset to defaults.";
    }

    public async Task ResetForSetupAsync()
    {
        settingsService.Current.HasCompletedOnboarding = false;
        await settingsService.ResetAsync();
        OnPropertyChanged(nameof(Settings));
        StatusMessage = "Settings reset. The setup walkthrough will open the next time Ray's Loader starts.";
    }

    public void CancelActiveUpdate() => updateCancellation?.Cancel();

    public void CancelActiveToolOperation() => toolCancellation?.Cancel();

    public void Dispose()
    {
        var cancellation = Interlocked.Exchange(ref updateCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        var toolOperation = Interlocked.Exchange(ref toolCancellation, null);
        toolOperation?.Cancel();
        toolOperation?.Dispose();
    }

    partial void OnIsUpdateBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDownloadUpdate));
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        DownloadAndInstallCommand.NotifyCanExecuteChanged();
        CancelUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasAvailableUpdateChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDownloadUpdate));
        DownloadAndInstallCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsToolBusyChanged(bool value)
    {
        CheckToolStatusCommand.NotifyCanExecuteChanged();
        UpdateYtDlpCommand.NotifyCanExecuteChanged();
        CancelToolOperationCommand.NotifyCanExecuteChanged();
    }

    private bool CanStartUpdateOperation() => !IsUpdateBusy;
    private bool CanStartDownloadOperation() => HasAvailableUpdate && !IsUpdateBusy;
    private bool CanCancelUpdate() => IsUpdateBusy && updateCancellation is not null;
    private bool CanStartToolOperation() => !IsToolBusy;
    private bool CanCancelToolOperation() => IsToolBusy && toolCancellation is not null;

    private bool TryBeginUpdateOperation(out CancellationTokenSource cancellation)
    {
        cancellation = new CancellationTokenSource();
        if (Interlocked.CompareExchange(ref updateCancellation, cancellation, null) is not null)
        {
            cancellation.Dispose();
            return false;
        }
        IsUpdateBusy = true;
        return true;
    }

    private void EndUpdateOperation(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(Interlocked.CompareExchange(ref updateCancellation, null, cancellation), cancellation))
            cancellation.Dispose();
        IsUpdateBusy = false;
    }

    private bool TryBeginToolOperation(out CancellationTokenSource cancellation)
    {
        cancellation = new CancellationTokenSource();
        if (Interlocked.CompareExchange(ref toolCancellation, cancellation, null) is not null)
        {
            cancellation.Dispose();
            return false;
        }
        IsToolBusy = true;
        return true;
    }

    private void EndToolOperation(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(Interlocked.CompareExchange(ref toolCancellation, null, cancellation), cancellation))
            cancellation.Dispose();
        IsToolBusy = false;
    }

    private void ApplyToolSnapshot(ToolHealthSnapshot snapshot)
    {
        YtDlpVersionText = FormatYtDlpVersion(snapshot);
        YtDlpSourceText = FormatYtDlpSource(snapshot);
        FfmpegVersionText = FormatFfmpegVersion(snapshot);
        JavaScriptRuntimeText = FormatJavaScriptRuntime(snapshot);
    }

    private static string CreateInitialUpdateStatus(IUpdateService service)
    {
        var messages = new List<string>();
        if (service.AvailableUpdate is { } available) messages.Add($"Version {available.Version} is available.");
        if (!service.LastCleanupReport.Succeeded)
            messages.Add($"Updater cleanup needs attention: {string.Join("; ", service.LastCleanupReport.Failures.Select(failure => $"{failure.FileName}: {failure.Message}"))}");
        return string.Join(" ", messages);
    }

    private static string FormatYtDlpVersion(ToolHealthSnapshot? snapshot) =>
        $"yt-dlp: {snapshot?.YtDlpVersion ?? "Not checked"}";

    private static string FormatYtDlpSource(ToolHealthSnapshot? snapshot) => snapshot is null
        ? "Source: Not checked"
        : $"Source: {snapshot.YtDlpSource} | {snapshot.YtDlpPath}";

    private static string FormatFfmpegVersion(ToolHealthSnapshot? snapshot) => snapshot is null
        ? "FFmpeg / ffprobe: Not checked"
        : $"FFmpeg: {snapshot.FfmpegVersion} | ffprobe: {snapshot.FfprobeVersion}";

    private static string FormatJavaScriptRuntime(ToolHealthSnapshot? snapshot) =>
        $"JavaScript runtime: {snapshot?.JavaScriptRuntime ?? "Not checked"}";
}
