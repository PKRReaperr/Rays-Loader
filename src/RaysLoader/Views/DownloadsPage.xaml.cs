using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using RaysLoader.Models;
using RaysLoader.Services;
using RaysLoader.ViewModels;

namespace RaysLoader.Views;

public sealed partial class DownloadsPage : Page
{
    private readonly IDownloadQueueService _queue;
    private readonly ISettingsService _settings;

    public DownloadsPage(MainViewModel viewModel, IDownloadQueueService queue, ISettingsService settings)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _queue = queue;
        _settings = settings;
        DataContext = viewModel;
        viewModel.AddDownloadRequested += AddDownloadRequested;
        viewModel.LogRequested += LogRequested;
        Unloaded += DownloadsPage_Unloaded;
    }

    public MainViewModel ViewModel { get; }

    public async Task ShowAddDialogAsync(string? initialUrl = null)
    {
        var dialogViewModel = App.Services.GetRequiredService<AddDownloadViewModel>();
        if (!string.IsNullOrWhiteSpace(initialUrl)) dialogViewModel.Urls = initialUrl;
        var dialog = new AddDownloadDialog(dialogViewModel, _queue) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private async void AddDownloadRequested(object? sender, EventArgs e) => await ShowAddDialogAsync();
    private async void LogRequested(object? sender, DownloadTask task) => await new LogDialog(task) { XamlRoot = XamlRoot }.ShowAsync();
    private void Stop_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is DownloadTask task) ViewModel.StopCommand.Execute(task); }
    private void Retry_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is DownloadTask task) ViewModel.RetryCommand.Execute(task); }
    private void ViewTerminal_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is DownloadTask task) ViewModel.ShowLogCommand.Execute(task); }

    private async void StopAllToggle_Click(object sender, RoutedEventArgs e)
    {
        if (StopAllToggle.IsChecked != true)
        {
            ViewModel.StopAllRemaining = false;
            return;
        }

        if (_settings.Current.ConfirmBeforeStoppingAll)
        {
            var confirmation = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Stop all downloads?",
                Content = "Active downloads will be cancelled and queued downloads will not start.",
                PrimaryButtonText = "Stop all",
                CloseButtonText = "Keep downloading",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            {
                StopAllToggle.IsChecked = false;
                return;
            }
        }

        ViewModel.StopAllRemaining = true;
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Text) || e.DataView.Contains(StandardDataFormats.WebLink))
            e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        string? value = null;
        if (e.DataView.Contains(StandardDataFormats.WebLink)) value = (await e.DataView.GetWebLinkAsync()).ToString();
        else if (e.DataView.Contains(StandardDataFormats.Text)) value = await e.DataView.GetTextAsync();
        if (!string.IsNullOrWhiteSpace(value)) await ShowAddDialogAsync(value.Trim());
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not DownloadTask task) return;
        var menu = new MenuFlyout();
        AddMenuItem(menu, "View log", "\uE8A5", () => ViewModel.ShowLogCommand.Execute(task));
        if (task.CanOpenFile) AddMenuItem(menu, "Open file", "\uE8E5", () => ViewModel.OpenFileCommand.Execute(task));
        AddMenuItem(menu, "Open containing folder", "\uE8B7", () => ViewModel.OpenFolderCommand.Execute(task));
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(menu, "Remove", "\uE74D", () => ViewModel.RemoveCommand.Execute(task));
        menu.ShowAt(button);
    }

    private static void AddMenuItem(MenuFlyout menu, string text, string glyph, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void DownloadsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.AddDownloadRequested -= AddDownloadRequested;
        ViewModel.LogRequested -= LogRequested;
    }
}
