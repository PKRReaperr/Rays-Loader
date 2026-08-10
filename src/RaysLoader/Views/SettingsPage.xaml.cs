using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaysLoader.Models;
using RaysLoader.Services;
using RaysLoader.Settings;
using RaysLoader.ViewModels;
using Windows.Storage.Pickers;

namespace RaysLoader.Views;

public sealed partial class SettingsPage : Page
{
    private readonly ISettingsService _settingsService;
    private readonly IAppDataCleanupService _cleanupService;
    private readonly IDownloadQueueService _downloadQueue;

    public SettingsPage(
        SettingsViewModel viewModel,
        ISettingsService settingsService,
        IAppDataCleanupService cleanupService,
        IDownloadQueueService downloadQueue)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _settingsService = settingsService;
        _cleanupService = cleanupService;
        _downloadQueue = downloadQueue;
        DataContext = viewModel;
        Unloaded += SettingsPage_Unloaded;
    }

    public SettingsViewModel ViewModel { get; }

    private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= SettingsPage_Unloaded;
        ViewModel.Dispose();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveCommand.ExecuteAsync(null);
        App.MainWindow.RefreshTheme();
    }

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.DownloadAndInstallCommand.ExecuteAsync(null);
        if (ViewModel.InstallerLaunched) App.MainWindow.Close();
    }

    private void AccentColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as RadioButtons)?.SelectedItem is not AppAccent accent) return;
        _settingsService.Current.AccentColor = accent;
        App.MainWindow.RefreshTheme();
    }

    private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);
        if (await picker.PickSingleFolderAsync() is { } folder) ViewModel.Settings.DefaultOutputDirectory = folder.Path;
    }

    private async void BrowseTool_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".exe");
        InitializePicker(picker);
        if (await picker.PickSingleFileAsync() is not { } file) return;
        if ((sender as FrameworkElement)?.Tag as string == "yt-dlp") ViewModel.Settings.YtDlpPath = file.Path;
        else ViewModel.Settings.FfmpegPath = file.Path;
    }

    private async void BrowseCookies_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".txt");
        InitializePicker(picker);
        if (await picker.PickSingleFileAsync() is { } file) ViewModel.Settings.CookiesFile = file.Path;
    }

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(_settingsService.SettingsDirectory);
    private void OpenToolsFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(Path.Combine(_settingsService.SettingsDirectory, "tools"));

    private async void CleanReset_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsUpdateBusy)
        {
            await ShowMessageAsync("Reset unavailable", "Cancel the active update operation and wait for it to finish before resetting app data.");
            return;
        }

        if (_downloadQueue.Downloads.Any(task => task.State is
            DownloadState.Queued or DownloadState.Preparing or DownloadState.Analyzing or
            DownloadState.Downloading or DownloadState.Processing or DownloadState.Paused))
        {
            await ShowMessageAsync("Reset unavailable", "Stop or finish all queued and active downloads before resetting app data.");
            return;
        }

        var resetSettings = new CheckBox { Content = "Reset settings and show setup again", IsChecked = true };
        var removeTools = new CheckBox { Content = "Remove managed yt-dlp and FFmpeg tools", IsChecked = true };
        var removeUpdates = new CheckBox { Content = "Clear update cache and rollback installers", IsChecked = true };
        var removeDiagnostics = new CheckBox { Content = "Remove crash logs and settings backups", IsChecked = true };
        var removeLegacy = new CheckBox { Content = "Remove legacy YtDlpGui app data", IsChecked = false };
        var dialog = new ContentDialog
        {
            Title = "Clean reset Ray's Loader?",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Choose what to reset. Downloads, user-selected tools, and cookie files are never deleted.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    resetSettings,
                    removeTools,
                    removeUpdates,
                    removeDiagnostics,
                    removeLegacy,
                },
            },
            PrimaryButtonText = "Reset selected data",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() is not ContentDialogResult.Primary) return;

        var targets = AppDataCleanupTargets.None;
        if (removeTools.IsChecked == true) targets |= AppDataCleanupTargets.ManagedTools;
        if (removeUpdates.IsChecked == true) targets |= AppDataCleanupTargets.UpdateArtifacts;
        if (removeDiagnostics.IsChecked == true) targets |= AppDataCleanupTargets.Diagnostics;
        if (removeLegacy.IsChecked == true) targets |= AppDataCleanupTargets.LegacyData;

        if (resetSettings.IsChecked == true)
        {
            try
            {
                await ViewModel.ResetForSetupAsync();
                App.MainWindow.RefreshTheme();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
            {
                ViewModel.StatusMessage = "Settings could not be reset.";
                await ShowMessageAsync("Reset failed", $"No managed data was removed because settings could not be reset:{Environment.NewLine}{Environment.NewLine}{exception.Message}");
                return;
            }
        }

        var report = _cleanupService.Cleanup(targets);

        if (report.Succeeded)
        {
            ViewModel.StatusMessage = "Selected app data was reset successfully.";
            await ShowMessageAsync("Reset complete", "Selected Ray's Loader data was removed. Restart the app to run setup again.");
            return;
        }

        var failures = string.Join(Environment.NewLine, report.Failures.Select(failure => $"{failure.Path}: {failure.Message}"));
        ViewModel.StatusMessage = "Some app data could not be removed.";
        await ShowMessageAsync("Reset incomplete", $"Some files could not be removed:{Environment.NewLine}{Environment.NewLine}{failures}");
    }

    private async void OpenWindowsUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (!await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:appsfeatures")))
            await ShowMessageAsync("Windows Settings unavailable", "Open Windows Settings, select Apps, then Installed apps to uninstall Ray's Loader.");
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "Close",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static void InitializePicker(object picker) => WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
}
