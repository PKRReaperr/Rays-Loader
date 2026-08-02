using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using RaysLoader.Settings;
using RaysLoader.Theming;

namespace RaysLoader.Converters;

public sealed class AccentColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        new SolidColorBrush(AccentPalette.Color(value is AppAccent accent ? accent : AppAccent.System));

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed class AccentColorToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        AccentPalette.Label(value is AppAccent accent ? accent : AppAccent.System);

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
