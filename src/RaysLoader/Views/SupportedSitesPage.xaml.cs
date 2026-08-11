using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaysLoader.Models;
using RaysLoader.ViewModels;

namespace RaysLoader.Views;

public sealed partial class SupportedSitesPage : Page
{
    public SupportedSitesPage(SupportedSitesViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        Loaded += SupportedSitesPage_Loaded;
        Unloaded += SupportedSitesPage_Unloaded;
    }

    public SupportedSitesViewModel ViewModel { get; }

    private async void SupportedSitesPage_Loaded(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();

    private void SupportedSitesPage_Unloaded(object sender, RoutedEventArgs e) => ViewModel.CancelLoading();

    private async void KnownIssue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SupportedSite site } || XamlRoot is null)
        {
            return;
        }

        var sourceName = string.Equals(site.SiteName, site.ExtractorName, StringComparison.OrdinalIgnoreCase)
            ? site.ExtractorName
            : $"{site.ExtractorName} ({site.SiteName})";
        var details = new StackPanel { Spacing = 12, MaxWidth = 500 };
        details.Children.Add(new InfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = InfoBarSeverity.Warning,
            Title = "Currently marked as broken",
            Message = "This status comes from the extractor list in your installed copy of yt-dlp.",
        });
        details.Children.Add(new TextBlock
        {
            Text = $"Source: {sourceName}\nInstalled yt-dlp: {ViewModel.YtDlpVersion}",
            TextWrapping = TextWrapping.Wrap,
        });
        details.Children.Add(new TextBlock
        {
            Text = "yt-dlp's local extractor list does not include an issue number or first-discovered date. " +
                "Related issue reports are opened oldest-first so you can find the original report, creation date, updates, and discussion.",
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"{site.ExtractorName} issue details",
            Content = details,
            PrimaryButtonText = "View issue reports",
            SecondaryButtonText = "Open support listing",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        var destination = result switch
        {
            ContentDialogResult.Primary => SupportedSiteLinks.CreateIssueSearch(site),
            ContentDialogResult.Secondary => SupportedSiteLinks.OfficialSupportedSites,
            _ => null,
        };

        if (destination is not null)
        {
            await Windows.System.Launcher.LaunchUriAsync(destination);
        }
    }
}
