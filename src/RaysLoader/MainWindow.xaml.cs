using Microsoft.Extensions.DependencyInjection;
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
        AppLogo.Source = new BitmapImage(new Uri(AppIdentity.LogoUri));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        AppNameText.Text = AppIdentity.DisplayName.ToUpperInvariant();
        VersionText.Text = VersionLabel();
        ApplyTheme(settingsService.Current.Theme);
        AccentPalette.Apply(settingsService.Current.AccentColor);
        ApplyCaptionButtonColors();
        if (AppIdentity.IsDevelopment)
        {
            DevelopmentBadge.Visibility = Visibility.Visible;
            AppUpdatesMenuItem.Text = "Download engine updates";
        }

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", AppIdentity.IconFileName));
        RestoreWindowPlacement(settingsService.Current);
        AppWindow.Closing += AppWindow_Closing;
        AllItem.IsChecked = true;
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
        AccentPalette.Apply(_settingsService.Current.AccentColor);
    }

    public void ShowSupportedSites()
    {
        if (ContentFrame.Content is not SupportedSitesPage)
            ContentFrame.Content = App.Services.GetRequiredService<SupportedSitesPage>();
        SupportedSitesItem.IsChecked = true;
    }

    private void Rail_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as RadioButton)?.CommandParameter is not string route) return;
        if (Enum.TryParse<DownloadFilter>(route, out var filter))
        {
            ViewModel.SelectedFilter = filter;
            ShowDownloads();
        }
        else if (route == "Settings") ShowSettings();
        else if (route == "About") ShowAbout();
        else if (route == "SupportedSites") ShowSupportedSites();
    }

    private void ShowAbout()
    {
        if (ContentFrame.Content is not AboutPage) ContentFrame.Content = App.Services.GetRequiredService<AboutPage>();
        AboutItem.IsChecked = true;
    }

    private async void AddDownload_Click(object sender, RoutedEventArgs e) => await ShowAddDownloadAsync();
    private async void PasteAndAdd_Click(object sender, RoutedEventArgs e) => await ShowAddDownloadAsync(await ClipboardTextAsync());
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void ShowDownloads_Click(object sender, RoutedEventArgs e) { AllItem.IsChecked = true; ShowDownloads(); }
    private void ShowSettings_Click(object sender, RoutedEventArgs e) => ShowSettings();
    private void ShowAbout_Click(object sender, RoutedEventArgs e) => ShowAbout();
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
        await dialog.ShowThemedAsync();
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
            Rail.IsHitTestVisible = false;
            RootGrid.IsHitTestVisible = false;
            var autoSave = App.Services.GetRequiredService<SettingsAutoSaveService>();
            CaptureWindowPlacement(sender, _settingsService.Current);
            var saved = await autoSave.FlushAsync();
            if (!saved)
            {
                Rail.IsHitTestVisible = true;
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
                try { choice = await dialog.ShowThemedAsync(); }
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
                Rail.IsHitTestVisible = false;
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
        CaptureWindowPlacement(sender, _settingsService.Current);
        try { await _settingsService.SaveAsync(_settingsService.Current); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(exception); }
    }

    /// <summary>Minimizes the window when the user enabled Start minimized and setup is complete.</summary>
    public void ApplyStartupWindowState()
    {
        var settings = _settingsService.Current;
        if (settings.StartMinimized && settings.HasCompletedOnboarding && AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.Minimize();
    }

    private void RestoreWindowPlacement(AppSettings settings)
    {
        if (!settings.RememberWindowPlacement)
        {
            var defaults = new AppSettings();
            AppWindow.Resize(new SizeInt32(defaults.WindowWidth, defaults.WindowHeight));
            return;
        }

        var size = new SizeInt32(settings.WindowWidth, settings.WindowHeight);
        if (settings.WindowX is { } x && settings.WindowY is { } y &&
            DisplayArea.GetFromRect(new RectInt32(x, y, size.Width, size.Height), DisplayAreaFallback.None) is not null)
        {
            // Only restore a position that is still on a connected display.
            AppWindow.MoveAndResize(new RectInt32(x, y, size.Width, size.Height));
        }
        else
        {
            AppWindow.Resize(size);
        }
    }

    private static void CaptureWindowPlacement(AppWindow window, AppSettings settings)
    {
        if (!settings.RememberWindowPlacement) return;
        // A minimized or maximized window reports bounds that make a poor restore
        // size, so keep the last placement recorded in the normal state.
        if (window.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Restored }) return;
        settings.WindowWidth = window.Size.Width;
        settings.WindowHeight = window.Size.Height;
        settings.WindowX = window.Position.X;
        settings.WindowY = window.Position.Y;
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
        SettingsNavigationItem.IsChecked = true;
        return page;
    }

    private static string VersionLabel()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version;
        return version is null ? string.Empty : $"V{version.Major}.{version.Minor}.{version.Build}";
    }

    private void RootGrid_ActualThemeChanged(FrameworkElement sender, object args) => ApplyCaptionButtonColors();

    /// <summary>Keeps the system caption buttons readable on the flat title bar in both themes.</summary>
    private void ApplyCaptionButtonColors()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        var titleBar = AppWindow.TitleBar;
        var light = RootGrid.ActualTheme == ElementTheme.Light;
        var foreground = light ? Windows.UI.Color.FromArgb(255, 11, 11, 11) : Windows.UI.Color.FromArgb(255, 237, 237, 237);
        var hover = light ? Windows.UI.Color.FromArgb(255, 223, 222, 215) : Windows.UI.Color.FromArgb(255, 27, 27, 27);
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = hover;
        titleBar.ButtonInactiveForegroundColor = light ? Windows.UI.Color.FromArgb(255, 92, 92, 92) : Windows.UI.Color.FromArgb(255, 140, 140, 140);
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
