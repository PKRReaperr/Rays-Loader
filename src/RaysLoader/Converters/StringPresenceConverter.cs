using Microsoft.UI.Xaml.Data;

namespace RaysLoader.Converters;

public sealed class StringPresenceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is string text && !string.IsNullOrWhiteSpace(text);
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
