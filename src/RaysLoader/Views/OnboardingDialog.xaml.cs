using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using RaysLoader.Services;
using RaysLoader.Settings;
using Windows.Storage.Pickers;

namespace RaysLoader.Views;

public sealed partial class OnboardingDialog : ContentDialog
{
    private static readonly string[] VideoQualities = ["best", "2160p", "1440p", "1080p", "720p", "480p", "360p"];
    private static readonly string[] Containers = ["mp4", "mkv", "webm"];
    private readonly ISettingsService settingsService;
    private readonly AppSettings settings;
    private int currentStep = 1;
    private string selectedOutputDirectory;

    public OnboardingDialog(ISettingsService settingsService)
    {
        InitializeComponent();
        this.settingsService = settingsService;
        settings = settingsService.Current;
        selectedOutputDirectory = settings.DefaultOutputDirectory;

        WelcomeLogo.Source = new BitmapImage(new Uri(AppIdentity.LogoUri));
        WelcomeTitle.Text = $"Welcome to {AppIdentity.DisplayName}";
        FolderTextBox.Text = selectedOutputDirectory;
        QualityComboBox.ItemsSource = VideoQualities;
        QualityComboBox.SelectedItem = VideoQualities.Contains(settings.DefaultVideoQuality) ? settings.DefaultVideoQuality : "best";
        ContainerComboBox.ItemsSource = Containers;
        ContainerComboBox.SelectedItem = Containers.Contains(settings.DefaultContainer) ? settings.DefaultContainer : "mp4";
        ConcurrentDownloadsBox.Value = settings.MaximumSimultaneousDownloads;
        PlaylistCheckBox.IsChecked = settings.DownloadPlaylists;
        MetadataCheckBox.IsChecked = settings.EmbedMetadata;
        UpdateStep();
    }

    private void PrimaryButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (currentStep < 4)
        {
            args.Cancel = true;
            currentStep++;
            UpdateStep();
            return;
        }

        SaveAndComplete(args);
    }

    private void SecondaryButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (currentStep > 1)
        {
            currentStep--;
            UpdateStep();
        }
    }

    private void CloseButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args) => SaveAndComplete(args);

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        if (await picker.PickSingleFolderAsync() is not { } folder) return;
        selectedOutputDirectory = folder.Path;
        FolderTextBox.Text = selectedOutputDirectory;
    }

    private void ClearFolder_Click(object sender, RoutedEventArgs e)
    {
        selectedOutputDirectory = string.Empty;
        FolderTextBox.Text = string.Empty;
    }

    private async void SaveAndComplete(ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            settings.DefaultOutputDirectory = selectedOutputDirectory;
            settings.DefaultVideoQuality = QualityComboBox.SelectedItem as string ?? "best";
            settings.DefaultContainer = ContainerComboBox.SelectedItem as string ?? "mp4";
            settings.MaximumSimultaneousDownloads = (int)Math.Clamp(ConcurrentDownloadsBox.Value, 1, 8);
            settings.DownloadPlaylists = PlaylistCheckBox.IsChecked == true;
            settings.EmbedMetadata = MetadataCheckBox.IsChecked == true;
            settings.HasCompletedOnboarding = true;
            await settingsService.SaveAsync(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            args.Cancel = true;
            SaveErrorBar.Message = $"Settings could not be saved: {exception.Message}";
            SaveErrorBar.IsOpen = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void UpdateStep()
    {
        WelcomeStep.Visibility = currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        FolderStep.Visibility = currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        DefaultsStep.Visibility = currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        ReadyStep.Visibility = currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;
        StepProgress.Value = currentStep;
        StepLabel.Text = $"STEP {currentStep} OF 4";
        StepName.Text = currentStep switch
        {
            1 => "Welcome",
            2 => "Folder",
            3 => "Defaults",
            _ => "Finish",
        };
        PrimaryButtonText = currentStep == 4 ? "Finish" : "Next";
        SecondaryButtonText = currentStep > 1 ? "Back" : string.Empty;
        CloseButtonText = currentStep == 4 ? string.Empty : "Set up later";
        FolderSummary.Text = string.IsNullOrWhiteSpace(selectedOutputDirectory)
            ? "No default folder selected. Ray's Loader will ask for a destination when you add a download."
            : $"Downloads will default to: {selectedOutputDirectory}";
    }
}
