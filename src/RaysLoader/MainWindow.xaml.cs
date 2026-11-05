using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices;
using RaysLoader.ProcessManagement;
using RaysLoader.Services;
using RaysLoader.Settings;
using RaysLoader.Theming;
using RaysLoader.ViewModels;
using RaysLoader.Views;
using Windows.Graphics;
#if RAYSLOADER_DEV
using SettingsView = RaysLoader.Views.DevSettingsPage;
#else
using SettingsView = RaysLoader.Views.SettingsPage;
#endif

namespace RaysLoader;

public sealed partial class MainWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly IUpdateService _updateService;
    private readonly IToolMaintenanceService _toolMaintenanceService;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _startupFlowStarted;
    private bool _closeAfterSave;
    private bool _closeInProgress;

    public MainWindow(
        MainViewModel viewModel,
        ISettingsService settingsService,
        IUpdateService updateService,
        IToolMaintenanceService toolMaintenanceService)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _settingsService = settingsService;
        _updateService = updateService;
        _toolMaintenanceService = toolMaintenanceService;
        RootGrid.DataContext = viewModel;
        Title = AppIdentity.DisplayName;
        AppNameText.Text = AppIdentity.DisplayName;
        AppLogo.Source = new BitmapImage(new Uri(AppIdentity.LogoUri));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        TrySetBackdrop();
        ApplyTheme(settingsService.Current.Theme);
        AccentPalette.Apply(settingsService.Current.AccentColor, settingsService.Current.Theme);
        if (AppIdentity.IsDevelopment)
        {
            DevelopmentBadge.Visibility = Visibility.Visible;
            AppUpdatesMenuItem.Text = "Download engine updates";
            Navigation.PaneDisplayMode = NavigationViewPaneDisplayMode.Auto;
            Navigation.OpenPaneLength = 212;
            Navigation.ExpandedModeThresholdWidth = 1100;
            Navigation.CompactModeThresholdWidth = 700;
            AllItem.Content = "All downloads";
        }

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", AppIdentity.IconFileName));
        AppWindow.Resize(new SizeInt32(settingsService.Current.WindowWidth, settingsService.Current.WindowHeight));
        AppWindow.Closing += AppWindow_Closing;
        Navigation.SelectedItem = AllItem;
        ShowDownloads();
        RootGrid.Loaded += RootGrid_Loaded;
    }

    public MainViewModel ViewModel { get; }

    public void ShowDownloads()
    {
        if (ContentFrame.Content is not DownloadsPage) ContentFrame.Content = App.Services.GetRequiredService<DownloadsPage>();
    }

    public async Task ShowAddDownloadAsync(string? initialUrl = null)
    {
        ShowDownloads();
        if (ContentFrame.Content is DownloadsPage page) await page.ShowAddDialogAsync(initialUrl);
    }

    public void RefreshTheme()
    {
        ApplyTheme(_settingsService.Current.Theme);
        AccentPalette.Apply(_settingsService.Current.AccentColor, _settingsService.Current.Theme);
    }

    public void ShowSupportedSites()
    {
        Navigation.SelectedItem = SupportedSitesItem;
        if (ContentFrame.Content is not SupportedSitesPage)
            ContentFrame.Content = App.Services.GetRequiredService<SupportedSitesPage>();
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag) return;
        if (Enum.TryParse<DownloadFilter>(tag, out var filter))
        {
            ViewModel.SelectedFilter = filter;
            ShowDownloads();
        }
        else if (tag == "Settings") ShowSettings();
        else if (tag == "About") ContentFrame.Content = App.Services.GetRequiredService<AboutPage>();
        else if (tag == "SupportedSites") ShowSupportedSites();
    }

    private async void AddDownload_Click(object sender, RoutedEventArgs e) => await ShowAddDownloadAsync();
    private async void PasteAndAdd_Click(object sender, RoutedEventArgs e) => await ShowAddDownloadAsync(await ClipboardTextAsync());
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void ShowDownloads_Click(object sender, RoutedEventArgs e) { Navigation.SelectedItem = AllItem; ShowDownloads(); }
    private void ShowSettings_Click(object sender, RoutedEventArgs e) => ShowSettings();
    private void ShowAbout_Click(object sender, RoutedEventArgs e) => ContentFrame.Content = App.Services.GetRequiredService<AboutPage>();
    private void ShowSupportedSites_Click(object sender, RoutedEventArgs e) => ShowSupportedSites();
    private async void GettingStarted_Click(object sender, RoutedEventArgs e) => await ShowOnboardingAsync();
    private async void ReportBug_Click(object sender, RoutedEventArgs e) => await OpenWebLinkAsync(AppLinks.BugReport);
    private async void SuggestFeature_Click(object sender, RoutedEventArgs e) => await OpenWebLinkAsync(AppLinks.FeatureRequest);
    private async void ViewFeedback_Click(object sender, RoutedEventArgs e) => await OpenWebLinkAsync(AppLinks.Issues);
    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        var page = ShowSettings();
        if (AppIdentity.IsDevelopment)
        {
#if RAYSLOADER_DEV
            page.ShowDownloadEngine();
#endif
        }
        else await page.ViewModel.CheckForUpdatesCommand.ExecuteAsync(null);
    }
    private void OpenUpdates_Click(object sender, RoutedEventArgs e)
    {
        UpdateInfoBar.IsOpen = false;
        ShowSettings();
    }
    private void RetryAll_Click(object sender, RoutedEventArgs e) => ViewModel.RetryAllFailedCommand.Execute(null);
    private void StopAll_Click(object sender, RoutedEventArgs e) => ViewModel.StopAllCommand.Execute(null);

    private async void RootGrid_Loaded(object sender, RoutedEventArgs args)
    {
        if (_startupFlowStarted) return;
        _startupFlowStarted = true;
        RootGrid.Loaded -= RootGrid_Loaded;
        if (!_settingsService.Current.HasCompletedOnboarding) await ShowOnboardingAsync();
        if (_settingsService.Current.AutomaticallyUpdateYtDlp)
        {
            try
            {
                await _toolMaintenanceService.CheckAndUpdateYtDlpAsync(
                    _settingsService.Current.YtDlpUpdateChannel,
                    _lifetimeCancellation.Token);
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or YtDlpException or MissingToolException or ProcessLaunchException)
            {
                System.Diagnostics.Debug.WriteLine(exception);
                UpdateInfoBar.Title = "The yt-dlp backend needs attention";
                UpdateInfoBar.Message = _toolMaintenanceService.LastMessage;
                UpdateInfoBar.Severity = InfoBarSeverity.Warning;
                UpdateInfoBar.IsOpen = true;
            }
        }
        if (AppIdentity.IsDevelopment || !_settingsService.Current.AutomaticallyCheckForUpdates) return;
        try
        {
            var update = await _updateService.CheckForUpdateAsync(_lifetimeCancellation.Token);
            if (update is null || RootGrid.XamlRoot is null) return;
            UpdateInfoBar.Title = $"{AppIdentity.DisplayName} {update.Version} is available";
            UpdateInfoBar.Message = "Open Settings to download, verify, and install the update.";
            UpdateInfoBar.Severity = InfoBarSeverity.Informational;
            UpdateInfoBar.IsOpen = true;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException or System.Text.Json.JsonException)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }
        catch (Exception exception)
        {
            // A background update check must never take down the main window.
            System.Diagnostics.Debug.WriteLine(exception);
        }
    }

    public async Task ShowOnboardingAsync()
    {
        var dialog = new OnboardingDialog(_settingsService) { XamlRoot = RootGrid.XamlRoot };
        await dialog.ShowAsync();
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (AppIdentity.IsDevelopment)
        {
            if (_closeAfterSave) return;
            // WinUI does not await async Closing handlers. Keep the window alive
            // until the final edits reach disk, including edits made just before X.
            args.Cancel = true;
            if (_closeInProgress) return;
            _closeInProgress = true;
            // Commit NumberBox text before disabling the editor. Keep settings
            // stable while cancelled tool processes finish restoring their files.
            SettingsNavigationItem.Focus(FocusState.Programmatic);
            Navigation.IsEnabled = false;
            RootGrid.IsHitTestVisible = false;
            var autoSave = App.Services.GetRequiredService<SettingsAutoSaveService>();
            _settingsService.Current.WindowWidth = sender.Size.Width;
            _settingsService.Current.WindowHeight = sender.Size.Height;
            var saved = await autoSave.FlushAsync();
            if (!saved)
            {
                Navigation.IsEnabled = true;
                RootGrid.IsHitTestVisible = true;
                var dialog = new ContentDialog
                {
                    Title = "Settings could not be saved",
                    Content = new TextBlock { Text = autoSave.StatusMessage, TextWrapping = TextWrapping.Wrap },
                    PrimaryButtonText = "Keep editing",
                    SecondaryButtonText = "Exit without saving",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = RootGrid.XamlRoot,
                };
                ContentDialogResult choice;
                try { choice = await dialog.ShowAsync(); }
                catch (Exception exception) when (exception is InvalidOperationException or COMException)
                {
                    // Another dialog can already be open when the title-bar X
                    // is clicked. Keep the editor usable and the edits in memory.
                    _closeInProgress = false;
                    UpdateInfoBar.Title = "Settings could not be saved";
                    UpdateInfoBar.Message = "Close the open dialog and correct the settings error before exiting.";
                    UpdateInfoBar.Severity = InfoBarSeverity.Error;
                    UpdateInfoBar.IsOpen = true;
                    return;
                }
                if (choice != ContentDialogResult.Secondary)
                {
                    _closeInProgress = false;
                    return;
                }
                Navigation.IsEnabled = false;
                RootGrid.IsHitTestVisible = false;
            }
            _lifetimeCancellation.Cancel();
            if (ContentFrame.Content is SettingsView devSettingsPage)
            {
                devSettingsPage.ViewModel.CancelActiveToolOperation();
                devSettingsPage.ViewModel.CancelActiveUpdate();
            }
            await _toolMaintenanceService.WaitForIdleAsync();
            autoSave.Dispose();
            _closeAfterSave = true;
            Close();
            return;
        }
        _lifetimeCancellation.Cancel();
        _updateService.CancelActiveOperations();
        if (ContentFrame.Content is SettingsView settingsPage)
        {
            settingsPage.ViewModel.CancelActiveUpdate();
            settingsPage.ViewModel.CancelActiveToolOperation();
        }
        var size = sender.Size;
        _settingsService.Current.WindowWidth = size.Width;
        _settingsService.Current.WindowHeight = size.Height;
        try { await _settingsService.SaveAsync(_settingsService.Current); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(exception); }
    }

    private void ApplyTheme(AppTheme theme) => RootGrid.RequestedTheme = theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    private SettingsView ShowSettings()
    {
        if (ContentFrame.Content is SettingsView existing) return existing;
        var page = App.Services.GetRequiredService<SettingsView>();
        ContentFrame.Content = page;
        Navigation.SelectedItem = SettingsNavigationItem;
        return page;
    }

    private void TrySetBackdrop()
    {
        try { SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base }; }
        catch (Exception exception) when (exception is COMException or InvalidOperationException) { System.Diagnostics.Debug.WriteLine(exception); }
    }

    private static async Task<string?> ClipboardTextAsync()
    {
        var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
        return content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)
            ? await content.GetTextAsync()
            : null;
    }

    private static async Task OpenWebLinkAsync(string url) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
}
