using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RaysLoader.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
    }

    public string VersionText => $"Version {typeof(AboutPage).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";

    private void OpenProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        var projectFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        if (!Directory.Exists(projectFolder)) projectFolder = AppContext.BaseDirectory;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(projectFolder) { UseShellExecute = true });
    }
}
