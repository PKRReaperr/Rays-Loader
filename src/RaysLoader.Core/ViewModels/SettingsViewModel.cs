using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RaysLoader.Services;
using RaysLoader.Settings;

namespace RaysLoader.ViewModels;

public sealed partial class SettingsViewModel(ISettingsService settingsService, IToolLocatorService toolLocator, IUpdateService updateService) : ObservableObject, IDisposable
{
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string updateStatus = CreateInitialUpdateStatus(updateService);
    [ObservableProperty] private double updateProgress;
    [ObservableProperty] private bool isUpdateBusy;
    [ObservableProperty] private bool hasAvailableUpdate = updateService.AvailableUpdate is not null;
    private CancellationTokenSource? updateCancellation;

    public string CurrentVersionText => $"Current version: {updateService.CurrentVersion}";
    public bool CanDownloadUpdate => HasAvailableUpdate && !IsUpdateBusy;
    public bool InstallerLaunched { get; private set; }

    public AppSettings Settings => settingsService.Current;
    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();
    public IReadOnlyList<AppAccent> AccentColors { get; } = Enum.GetValues<AppAccent>();

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

    [RelayCommand]
    private void ValidateTools()
    {
        var tools = toolLocator.Locate(Settings);
        if (tools.TrustWarnings.Count > 0)
        {
            StatusMessage = string.Join(" ", tools.TrustWarnings);
            return;
        }
        StatusMessage = File.Exists(tools.YtDlp) && File.Exists(tools.Ffmpeg)
            ? $"Tools found: {tools.YtDlp} and {tools.Ffmpeg}"
            : "One or more tools are missing. Select yt-dlp.exe and ffmpeg.exe or place them in the tools folder.";
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

    public void Dispose()
    {
        var cancellation = Interlocked.Exchange(ref updateCancellation, null);
        if (cancellation is null) return;
        cancellation.Cancel();
        cancellation.Dispose();
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

    private bool CanStartUpdateOperation() => !IsUpdateBusy;
    private bool CanStartDownloadOperation() => HasAvailableUpdate && !IsUpdateBusy;
    private bool CanCancelUpdate() => IsUpdateBusy && updateCancellation is not null;

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

    private static string CreateInitialUpdateStatus(IUpdateService service)
    {
        var messages = new List<string>();
        if (service.AvailableUpdate is { } available) messages.Add($"Version {available.Version} is available.");
        if (!service.LastCleanupReport.Succeeded)
            messages.Add($"Updater cleanup needs attention: {string.Join("; ", service.LastCleanupReport.Failures.Select(failure => $"{failure.FileName}: {failure.Message}"))}");
        return string.Join(" ", messages);
    }
}
