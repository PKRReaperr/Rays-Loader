using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RaysLoader.Services;
using RaysLoader.Settings;

namespace RaysLoader.ViewModels;

public sealed partial class SettingsViewModel(ISettingsService settingsService, IToolLocatorService toolLocator) : ObservableObject
{
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isBusy;

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
    private async Task ResetAsync()
    {
        await settingsService.ResetAsync();
        OnPropertyChanged(nameof(Settings));
        StatusMessage = "Settings reset to defaults.";
    }
}
