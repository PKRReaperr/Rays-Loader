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

public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;
    public AppAccent AccentColor { get; set; } = AppAccent.System;
    public bool HasCompletedOnboarding { get; set; }
    public string DefaultOutputDirectory { get; set; } = string.Empty;
    public int MaximumSimultaneousDownloads { get; set; } = 2;
    public bool ConfirmBeforeStoppingAll { get; set; } = true;
    public bool RememberWindowPlacement { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool ShowCompletionNotifications { get; set; } = true;
    public bool AutomaticallyCheckForUpdates { get; set; } = true;
    public string DefaultVideoQuality { get; set; } = "best";
    public string DefaultContainer { get; set; } = "mp4";
    public string DefaultAudioFormat { get; set; } = "mp3";
    public string DefaultAudioBitrate { get; set; } = "192K";
    public string FilenameTemplate { get; set; } = "%(title).200B [%(id)s].%(ext)s";
    public bool DownloadPlaylists { get; set; } = true;
    public bool WriteSubtitles { get; set; }
    public bool EmbedMetadata { get; set; }
    public bool WriteThumbnail { get; set; }
    public string YtDlpPath { get; set; } = string.Empty;
    public string FfmpegPath { get; set; } = string.Empty;
    public string CookiesFile { get; set; } = string.Empty;
    public string AdditionalArguments { get; set; } = string.Empty;
    public string Proxy { get; set; } = string.Empty;
    public string RateLimit { get; set; } = string.Empty;
    public int ConcurrentFragments { get; set; } = 1;
    public bool PreserveTemporaryFiles { get; set; }
    public bool VerboseLogging { get; set; }
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 760;
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
}
