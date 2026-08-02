using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using RaysLoader.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace RaysLoader.Views;

public sealed partial class LogDialog : ContentDialog
{
    private readonly DownloadTask _task;

    public LogDialog(DownloadTask task)
    {
        InitializeComponent();
        _task = task;
        DataContext = task;
        Title = "Live terminal";
        task.PropertyChanged += Task_PropertyChanged;
        Closed += (_, _) => task.PropertyChanged -= Task_PropertyChanged;
    }

    private void Copy_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var package = new DataPackage();
        package.SetText(_task.LogText);
        Clipboard.SetContent(package);
        args.Cancel = true;
    }

    private async void Save_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var picker = new FileSavePicker { SuggestedFileName = "RaysLoader-download.log" };
        picker.FileTypeChoices.Add("Log file", [".log"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        if (await picker.PickSaveFileAsync() is { } file) await FileIO.WriteTextAsync(file, _task.LogText);
    }

    private void Task_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadTask.LogText) && FollowOutputCheck.IsChecked is true)
        {
            LogTextBox.Select(LogTextBox.Text.Length, 0);
        }
    }
}
