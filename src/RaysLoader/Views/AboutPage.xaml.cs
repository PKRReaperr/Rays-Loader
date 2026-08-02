using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace RaysLoader.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        AboutLogo.Source = new BitmapImage(new Uri(AppIdentity.LogoUri));
        AboutNameText.Text = AppIdentity.DisplayName;
        AboutDescriptionText.Text = $"{AppIdentity.DisplayName} is a modern Windows 11 media downloader powered by yt-dlp.";
    }

    public string VersionText => $"Version {typeof(AboutPage).Assembly.GetName().Version?.ToString(3) ?? "1.3.0"}";

    private void OpenProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        var projectFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        if (!Directory.Exists(projectFolder)) projectFolder = AppContext.BaseDirectory;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(projectFolder) { UseShellExecute = true });
    }
}
