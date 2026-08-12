using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaysLoader.Services;
using RaysLoader.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace RaysLoader.Views;

public sealed partial class AddDownloadDialog : ContentDialog
{
    private const double PreferredContentWidth = 840;
    private const double MinimumContentWidth = 520;
    private const double PreferredScrollHeight = 620;
    private const double MinimumScrollHeight = 220;
    private readonly IDownloadQueueService _queue;
    private XamlRoot? _observedXamlRoot;

    public AddDownloadDialog(AddDownloadViewModel viewModel, IDownloadQueueService queue)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _queue = queue;
        DataContext = viewModel;
    }

    public AddDownloadViewModel ViewModel { get; }

    private void ContentPanel_Loaded(object sender, RoutedEventArgs e)
    {
        if (_observedXamlRoot != XamlRoot)
        {
            if (_observedXamlRoot is not null) _observedXamlRoot.Changed -= XamlRoot_Changed;
            _observedXamlRoot = XamlRoot;
            if (_observedXamlRoot is not null) _observedXamlRoot.Changed += XamlRoot_Changed;
        }
        UpdateResponsiveSize();
    }

    private void ContentPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_observedXamlRoot is not null) _observedXamlRoot.Changed -= XamlRoot_Changed;
        _observedXamlRoot = null;
        ViewModel.Dispose();
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateResponsiveSize();

    private void UpdateResponsiveSize()
    {
        if (XamlRoot is null) return;
        var viewport = XamlRoot.Size;
        ContentPanel.Width = Math.Clamp((viewport.Width * 0.68) - 60, MinimumContentWidth, PreferredContentWidth);
        OptionsScrollViewer.MaxHeight = Math.Clamp(viewport.Height - 180, MinimumScrollHeight, PreferredScrollHeight);
    }

    private async void RetryPreview_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.AnalyzeCommand.ExecuteAsync(null);
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
                var preview = ViewModel.FindPreview(request.Url);
                var task = _queue.Enqueue(request, preview?.Title, startImmediately: false);
                if (preview is not null) task.ThumbnailUrl = preview.ThumbnailUrl;
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

    private async void OpenSourceWebsite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(ViewModel.GetSourceWebsiteUri());
        }
        catch (FormatException exception)
        {
            ViewModel.ErrorMessage = exception.Message;
        }
    }

    private static void InitializePicker(object picker)
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
    }
}
