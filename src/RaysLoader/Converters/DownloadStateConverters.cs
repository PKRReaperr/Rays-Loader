using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using RaysLoader.Models;

namespace RaysLoader.Converters;

public sealed class DownloadStateToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is DownloadState state ? state switch
    {
        DownloadState.Queued => "\uE823",
        DownloadState.Preparing or DownloadState.Analyzing => "\uE895",
        DownloadState.Downloading => "\uE896",
        DownloadState.Processing => "\uE895",
        DownloadState.Paused => "\uE769",
        DownloadState.Completed => "\uE73E",
        DownloadState.Failed => "\uEA39",
        DownloadState.Cancelled => "\uE711",
        _ => "\uE946",
    } : "\uE946";

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed class DownloadStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => new SolidColorBrush(value is DownloadState state ? state switch
    {
        DownloadState.Completed => ColorHelper.FromArgb(255, 52, 199, 89),
        DownloadState.Failed => ColorHelper.FromArgb(255, 255, 99, 99),
        DownloadState.Cancelled => ColorHelper.FromArgb(255, 160, 160, 160),
        DownloadState.Paused => ColorHelper.FromArgb(255, 255, 185, 0),
        _ => ColorHelper.FromArgb(255, 96, 205, 255),
    } : Colors.Gray);

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
