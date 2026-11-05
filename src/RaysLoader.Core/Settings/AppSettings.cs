using CommunityToolkit.Mvvm.ComponentModel;

namespace RaysLoader.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public enum AppAccent
{
    System,
    Ocean,
    Mint,
    Coral,
    Gold,
    Rose,
    Violet,
}

public enum AuthenticationMode
{
    None,
    Browser,
    CookiesFile,
}

public enum YtDlpUpdateChannel
{
    Stable,
    Nightly,
}

public sealed partial class AppSettings : ObservableObject
{
    [ObservableProperty] private AppTheme theme = AppTheme.System;
    [ObservableProperty] private AppAccent accentColor = AppAccent.System;
    [ObservableProperty] private bool hasCompletedOnboarding;
    [ObservableProperty] private string defaultOutputDirectory = AppPaths.DefaultOutputDirectory;
    [ObservableProperty] private int maximumSimultaneousDownloads = 2;
    [ObservableProperty] private bool confirmBeforeStoppingAll = true;
    [ObservableProperty] private bool rememberWindowPlacement = true;
    [ObservableProperty] private bool startMinimized;
    [ObservableProperty] private bool showCompletionNotifications = true;
    [ObservableProperty] private bool automaticallyCheckForUpdates = true;
    [ObservableProperty] private bool automaticallyUpdateYtDlp = true;
    [ObservableProperty] private YtDlpUpdateChannel ytDlpUpdateChannel = YtDlpUpdateChannel.Nightly;
    [ObservableProperty] private string defaultVideoQuality = "best";
    [ObservableProperty] private string defaultContainer = "mp4";
    [ObservableProperty] private string defaultAudioFormat = "mp3";
    [ObservableProperty] private string defaultAudioBitrate = "192K";
    [ObservableProperty] private string filenameTemplate = "%(title).200B [%(id)s].%(ext)s";
    [ObservableProperty] private bool downloadPlaylists = true;
    [ObservableProperty] private bool writeSubtitles;
    [ObservableProperty] private bool embedMetadata;
    [ObservableProperty] private bool writeThumbnail;
    [ObservableProperty] private string ytDlpPath = string.Empty;
    [ObservableProperty] private string ffmpegPath = string.Empty;
    [ObservableProperty] private AuthenticationMode authenticationMode;
    [ObservableProperty] private string browserCookies = string.Empty;
    [ObservableProperty] private string cookiesFile = string.Empty;
    [ObservableProperty] private string additionalArguments = string.Empty;
    [ObservableProperty] private string proxy = string.Empty;
    [ObservableProperty] private string rateLimit = string.Empty;
    [ObservableProperty] private int concurrentFragments = 1;
    [ObservableProperty] private bool preserveTemporaryFiles;
    [ObservableProperty] private bool verboseLogging;
    [ObservableProperty] private int windowWidth = 1280;
    [ObservableProperty] private int windowHeight = 760;
    [ObservableProperty] private int? windowX;
    [ObservableProperty] private int? windowY;
}
