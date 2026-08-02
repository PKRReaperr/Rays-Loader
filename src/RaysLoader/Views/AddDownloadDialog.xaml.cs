using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaysLoader.Services;
using RaysLoader.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace RaysLoader.Views;

public sealed partial class AddDownloadDialog : ContentDialog
{
    private readonly IDownloadQueueService _queue;

    public AddDownloadDialog(AddDownloadViewModel viewModel, IDownloadQueueService queue)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _queue = queue;
        DataContext = viewModel;
    }

    public AddDownloadViewModel ViewModel { get; }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        AnalyzeButton.IsEnabled = false;
        try
        {
            await ViewModel.AnalyzeCommand.ExecuteAsync(null);
        }
        finally
        {
            AnalyzeButton.IsEnabled = true;
        }
    }

    private void PrimaryButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        => AddRequests(args, startQueue: false);

    private void SecondaryButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        => AddRequests(args, startQueue: true);

    private void AddRequests(ContentDialogButtonClickEventArgs args, bool startQueue)
    {
        try
        {
            var requests = ViewModel.CreateRequests();
            foreach (var request in requests)
            {
                var title = requests.Count == 1 && ViewModel.AnalysisTitle is not "Not analyzed"
                    ? ViewModel.AnalysisTitle
                    : null;
                var task = _queue.Enqueue(request, title, startImmediately: false);
                if (requests.Count == 1) task.ThumbnailUrl = ViewModel.AnalysisThumbnailUrl;
            }
            if (startQueue) _queue.StartQueued();
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ViewModel.ErrorMessage = exception.Message;
            args.Cancel = true;
        }
    }

    private async void Paste_Click(object sender, RoutedEventArgs e)
    {
        var content = Clipboard.GetContent();
        if (content.Contains(StandardDataFormats.Text)) ViewModel.Urls = await content.GetTextAsync();
    }

    private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);
        if (await picker.PickSingleFolderAsync() is { } folder) ViewModel.OutputDirectory = folder.Path;
    }

    private async void BrowseCookies_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".txt");
        InitializePicker(picker);
        if (await picker.PickSingleFileAsync() is { } file) ViewModel.CookiesFile = file.Path;
    }

    private static void InitializePicker(object picker)
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
    }
}
