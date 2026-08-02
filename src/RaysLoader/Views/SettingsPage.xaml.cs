using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaysLoader.Services;
using RaysLoader.Settings;
using RaysLoader.ViewModels;
using Windows.Storage.Pickers;

namespace RaysLoader.Views;

public sealed partial class SettingsPage : Page
{
    private readonly ISettingsService _settingsService;

    public SettingsPage(SettingsViewModel viewModel, ISettingsService settingsService)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _settingsService = settingsService;
        DataContext = viewModel;
    }

    public SettingsViewModel ViewModel { get; }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveCommand.ExecuteAsync(null);
        App.MainWindow.RefreshTheme();
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

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static void InitializePicker(object picker) => WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
}
