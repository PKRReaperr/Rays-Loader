using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using RaysLoader.Controls;
using RaysLoader.Models;

namespace RaysLoader.Converters;

/// <summary>Shows an element only when the bound value has non-blank text.</summary>
public sealed class StringPresenceToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is not null && !string.IsNullOrWhiteSpace(value.ToString()) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed class UppercaseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        (value?.ToString() ?? string.Empty).ToUpper(CultureInfo.CurrentCulture);

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>Formats a count as at least two digits ("05").</summary>
public sealed class TwoDigitConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int number ? number.ToString("00", CultureInfo.InvariantCulture) : value?.ToString() ?? string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>The uppercase status tag shown in the downloads table.</summary>
public sealed class DownloadStateToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is DownloadState state ? state.ToString().ToUpperInvariant() : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed class DownloadStateToToneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => ToneOf(value);

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();

    internal static TickTone ToneOf(object value) => value is DownloadState state ? state switch
    {
        DownloadState.Preparing or DownloadState.Analyzing or DownloadState.Downloading or DownloadState.Processing => TickTone.Active,
        DownloadState.Completed => TickTone.Complete,
        DownloadState.Failed => TickTone.Failed,
        _ => TickTone.Idle,
    } : TickTone.Idle;
}

/// <summary>Visible when the state's tone matches the parameter (Active, Complete, Failed or Idle).</summary>
public sealed class DownloadStateToneVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        Enum.TryParse<TickTone>(parameter as string, out var wanted) && DownloadStateToToneConverter.ToneOf(value) == wanted
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
