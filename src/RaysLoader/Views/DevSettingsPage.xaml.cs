using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaysLoader.Theming;
using RaysLoader.Models;
using RaysLoader.Services;
using RaysLoader.Settings;
using RaysLoader.ViewModels;
using Windows.Storage.Pickers;

namespace RaysLoader.Views;

public sealed partial class DevSettingsPage : Page
{
    private readonly ISettingsService _settingsService;
    private readonly IAppDataCleanupService _cleanupService;
    private readonly IDownloadQueueService _downloadQueue;
    private bool _ready;

    public DevSettingsPage(
        SettingsViewModel viewModel,
        ISettingsService settingsService,
        IAppDataCleanupService cleanupService,
        IDownloadQueueService downloadQueue,
        SettingsAutoSaveService autoSave)
    {
        ViewModel = viewModel;
        AutoSave = autoSave;
        _settingsService = settingsService;
        _cleanupService = cleanupService;
        _downloadQueue = downloadQueue;
        InitializeComponent();
        DataContext = viewModel;
        SettingsSections.SelectedItem = SettingsSections.MenuItems[0];
        _ready = true;
        Loaded += DevSettingsPage_Loaded;
        Unloaded += DevSettingsPage_Unloaded;
    }

    public SettingsViewModel ViewModel { get; }
    public SettingsAutoSaveService AutoSave { get; }
    public IReadOnlyList<string> VideoQualities { get; } = ["best", "2160p", "1440p", "1080p", "720p", "480p", "360p"];

    public void ShowDownloadEngine() => SettingsSections.SelectedItem = SettingsSections.MenuItems
        .OfType<NavigationViewItem>()
        .First(item => item.Tag as string == "engine");

    private void DevSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        AutoSave.PropertyChanged += AutoSave_PropertyChanged;
        RefreshSaveStatus();
    }

    private async void DevSettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        AutoSave.PropertyChanged -= AutoSave_PropertyChanged;
        ViewModel.Dispose();
        // The singleton keeps any pending save alive after navigation.
        try
        {
            await AutoSave.FlushAsync();
        }
        catch (ObjectDisposedException)
        {
            // Window shutdown has already flushed and disposed the singleton.
        }
    }

    private void AutoSave_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess) RefreshSaveStatus();
        else DispatcherQueue.TryEnqueue(RefreshSaveStatus);
    }

    private void RefreshSaveStatus()
    {
        SaveStatusText.Text = AutoSave.LastError is null
            ? AutoSave.StatusMessage
            : "Changes could not be saved";
        ToolTipService.SetToolTip(SaveStatusText, AutoSave.LastError?.Message);
        RetrySaveButton.Visibility = AutoSave.LastError is null ? Visibility.Collapsed : Visibility.Visible;
        RetrySaveButton.IsEnabled = !AutoSave.IsSaving;
    }

    private async void RetrySave_Click(object sender, RoutedEventArgs e) => await AutoSave.FlushAsync();

    private void SectionScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (AccentChoices is not null)
            AccentChoices.MaxColumns = e.NewSize.Width < 640 ? 2 : 4;
    }

    private void SettingsSections_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || AppearanceSection is null) return;
        var selected = item.Tag as string;
        AppearanceSection.Visibility = selected == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        DownloadsSection.Visibility = selected == "downloads" ? Visibility.Visible : Visibility.Collapsed;
        EngineSection.Visibility = selected == "engine" ? Visibility.Visible : Visibility.Collapsed;
        AuthenticationSection.Visibility = selected == "authentication" ? Visibility.Visible : Visibility.Collapsed;
        AdvancedSection.Visibility = selected == "advanced" ? Visibility.Visible : Visibility.Collapsed;
        SectionScrollViewer.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || (sender as ComboBox)?.SelectedItem is not AppTheme theme) return;
        _settingsService.Current.Theme = theme;
        App.MainWindow.RefreshTheme();
    }

    private void AccentColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || (sender as RadioButtons)?.SelectedItem is not AppAccent accent) return;
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
        if (ViewModel.IsUpdateBusy || ViewModel.IsToolBusy)
        {
            await ShowMessageAsync("Reset unavailable", "Cancel the active engine operation and wait for it to finish before resetting DEV app data.");
            return;
        }

        if (_downloadQueue.Downloads.Any(task => task.State is
            DownloadState.Queued or DownloadState.Preparing or DownloadState.Analyzing or
            DownloadState.Downloading or DownloadState.Processing or DownloadState.Paused))
        {
            await ShowMessageAsync("Reset unavailable", "Stop or finish all queued and active downloads before resetting DEV app data.");
            return;
        }

        var resetSettings = new CheckBox { Content = "Reset DEV settings and show setup again", IsChecked = true };
        var removeTools = new CheckBox { Content = "Remove DEV managed yt-dlp and FFmpeg tools", IsChecked = true };
        var removeUpdates = new CheckBox { Content = "Clear DEV update cache", IsChecked = true };
        var removeDiagnostics = new CheckBox { Content = "Remove DEV crash logs and settings backups", IsChecked = true };
        var dialog = new ContentDialog
        {
            Title = "Reset Ray's Loader DEV data?",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Choose what to reset in this DEV app. Downloads, user-selected tools, cookie files, and the main application's data are kept.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    resetSettings,
                    removeTools,
                    removeUpdates,
                    removeDiagnostics,
                },
            },
            PrimaryButtonText = "Reset selected data",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowThemedAsync() is not ContentDialogResult.Primary) return;
        if (!await AutoSave.FlushAsync())
        {
            await ShowMessageAsync("Reset unavailable", "Your current settings could not be saved. Retry saving before resetting DEV app data.");
            return;
        }

        var targets = AppDataCleanupTargets.None;
        if (removeTools.IsChecked == true) targets |= AppDataCleanupTargets.ManagedTools;
        if (removeUpdates.IsChecked == true) targets |= AppDataCleanupTargets.UpdateArtifacts;
        if (removeDiagnostics.IsChecked == true) targets |= AppDataCleanupTargets.Diagnostics;

        if (resetSettings.IsChecked == true)
        {
            try
            {
                await ViewModel.ResetForSetupAsync();
                App.MainWindow.RefreshTheme();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
            {
                ViewModel.StatusMessage = "DEV settings could not be reset.";
                await ShowMessageAsync("Reset failed", $"No managed data was removed because settings could not be reset:{Environment.NewLine}{Environment.NewLine}{exception.Message}");
                return;
            }
        }

        var report = _cleanupService.Cleanup(targets);
        if (report.Succeeded)
        {
            ViewModel.StatusMessage = "Selected DEV app data was reset successfully.";
            await ShowMessageAsync("Reset complete", resetSettings.IsChecked == true
                ? "Selected DEV data was removed. Restart Ray's Loader DEV to run setup again."
                : "Selected DEV data was removed.");
            return;
        }

        var failures = string.Join(Environment.NewLine, report.Failures.Select(failure => $"{failure.Path}: {failure.Message}"));
        ViewModel.StatusMessage = "Some DEV app data could not be removed.";
        await ShowMessageAsync("Reset incomplete", $"Some files could not be removed:{Environment.NewLine}{Environment.NewLine}{failures}");
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
        await dialog.ShowThemedAsync();
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static void InitializePicker(object picker) => WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
}
