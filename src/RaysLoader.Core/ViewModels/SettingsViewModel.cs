using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RaysLoader.Services;
using RaysLoader.Settings;

namespace RaysLoader.ViewModels;

public sealed partial class SettingsViewModel(ISettingsService settingsService, IToolLocatorService toolLocator, IUpdateService updateService) : ObservableObject
{
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string updateStatus = updateService.AvailableUpdate is { } available
        ? $"Version {available.Version.ToString(3)} is available."
        : string.Empty;
    [ObservableProperty] private double updateProgress;
    [ObservableProperty] private bool isUpdateBusy;
    [ObservableProperty] private bool hasAvailableUpdate = updateService.AvailableUpdate is not null;

    public string CurrentVersionText => $"Current version: {updateService.CurrentVersion.ToString(3)}";
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
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
        StatusMessage = File.Exists(tools.YtDlp) && File.Exists(tools.Ffmpeg)
            ? $"Tools found: {tools.YtDlp} and {tools.Ffmpeg}"
            : "One or more tools are missing. Select yt-dlp.exe and ffmpeg.exe or place them in the tools folder.";
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        IsUpdateBusy = true;
        UpdateProgress = 0;
        UpdateStatus = "Checking GitHub for updates...";
        try
        {
            var update = await updateService.CheckForUpdateAsync();
            HasAvailableUpdate = update is not null;
            UpdateStatus = update is null
                ? "Ray's Loader is up to date."
                : $"Version {update.Version.ToString(3)} is available.";
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException or System.Text.Json.JsonException)
        {
            HasAvailableUpdate = false;
            UpdateStatus = $"Update check failed: {exception.Message}";
        }
        finally
        {
            IsUpdateBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadAndInstallAsync()
    {
        var update = updateService.AvailableUpdate;
        if (update is null) return;

        IsUpdateBusy = true;
        InstallerLaunched = false;
        UpdateProgress = 0;
        UpdateStatus = $"Downloading version {update.Version.ToString(3)}...";
        try
        {
            var progress = new Progress<double>(value => UpdateProgress = value);
            var installer = await updateService.DownloadInstallerAsync(update, progress);
            UpdateStatus = "Installer verified. Starting the upgrade...";
            updateService.LaunchInstaller(installer);
            InstallerLaunched = true;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or TaskCanceledException or System.ComponentModel.Win32Exception)
        {
            UpdateStatus = $"Update failed: {exception.Message}";
        }
        finally
        {
            IsUpdateBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResetAsync()
    {
        await settingsService.ResetAsync();
        OnPropertyChanged(nameof(Settings));
        StatusMessage = "Settings reset to defaults.";
    }
}
