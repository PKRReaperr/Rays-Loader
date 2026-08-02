using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using RaysLoader.Services;
using RaysLoader.Settings;
using RaysLoader.Theming;
using RaysLoader.ViewModels;
using RaysLoader.Views;
using Windows.Graphics;

namespace RaysLoader;

public sealed partial class MainWindow : Window
{
    private readonly ISettingsService _settingsService;

    public MainWindow(MainViewModel viewModel, ISettingsService settingsService)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _settingsService = settingsService;
        RootGrid.DataContext = viewModel;
        Title = "Ray's Loader";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        TrySetBackdrop();
        ApplyTheme(settingsService.Current.Theme);
        AccentPalette.Apply(settingsService.Current.AccentColor, settingsService.Current.Theme);

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "RaysLoader.ico"));
        AppWindow.Resize(new SizeInt32(settingsService.Current.WindowWidth, settingsService.Current.WindowHeight));
        AppWindow.Closing += AppWindow_Closing;
        Navigation.SelectedItem = AllItem;
        ShowDownloads();
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

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag) return;
        if (Enum.TryParse<DownloadFilter>(tag, out var filter))
        {
            ViewModel.SelectedFilter = filter;
            ShowDownloads();
        }
        else if (tag == "Settings") ContentFrame.Content = App.Services.GetRequiredService<SettingsPage>();
        else if (tag == "About") ContentFrame.Content = App.Services.GetRequiredService<AboutPage>();
    }

    private async void AddDownload_Click(object sender, RoutedEventArgs e) => await ShowAddDownloadAsync();
    private async void PasteAndAdd_Click(object sender, RoutedEventArgs e) => await ShowAddDownloadAsync(await ClipboardTextAsync());
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void ShowDownloads_Click(object sender, RoutedEventArgs e) { Navigation.SelectedItem = AllItem; ShowDownloads(); }
    private void ShowSettings_Click(object sender, RoutedEventArgs e) => ContentFrame.Content = App.Services.GetRequiredService<SettingsPage>();
    private void ShowAbout_Click(object sender, RoutedEventArgs e) => ContentFrame.Content = App.Services.GetRequiredService<AboutPage>();
    private void RetryAll_Click(object sender, RoutedEventArgs e) => ViewModel.RetryAllFailedCommand.Execute(null);
    private void StopAll_Click(object sender, RoutedEventArgs e) => ViewModel.StopAllCommand.Execute(null);

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
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
}
