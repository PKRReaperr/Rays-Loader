using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace RaysLoader.Theming;

public static class DialogTheme
{
    /// <summary>
    /// Shows the dialog in the window's current theme. Dialogs open in the popup
    /// layer, which does not inherit the RequestedTheme set on the window content.
    /// </summary>
    public static IAsyncOperation<ContentDialogResult> ShowThemedAsync(this ContentDialog dialog)
    {
        if (dialog.XamlRoot?.Content is FrameworkElement root)
            dialog.RequestedTheme = root.ActualTheme;
        return dialog.ShowAsync();
    }
}
