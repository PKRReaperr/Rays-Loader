namespace RaysLoader.Models;

public enum DownloadMode
{
    Video,
    VideoOnly,
    AudioOnly,
}

public enum ClipDownloadMethod
{
    DownloadThenTrim,
    PartialDownload,
    FastPartialDownload,
}

public sealed record TrimRange(
    bool Enabled = false,
    double StartSeconds = 0,
    double EndSeconds = 0,
    bool ExactBoundaries = false,
    ClipDownloadMethod Method = ClipDownloadMethod.DownloadThenTrim);

public sealed record DownloadRequest
{
    public required string Url { get; init; }
    public required string OutputDirectory { get; init; }
    public DownloadMode Mode { get; init; } = DownloadMode.Video;
    public string VideoQuality { get; init; } = "best";
    public string VideoFormatId { get; init; } = string.Empty;
    public string AudioFormatId { get; init; } = string.Empty;
    public string Container { get; init; } = "mp4";
    public string AudioFormat { get; init; } = "mp3";
    public string AudioBitrate { get; init; } = "192K";
    public bool WriteSubtitles { get; init; }
    public string SubtitleLanguages { get; init; } = "all,-live_chat";
    public bool EmbedMetadata { get; init; }
    public bool WriteThumbnail { get; init; }
    public bool DownloadPlaylist { get; init; } = true;
    public string CookiesFile { get; init; } = string.Empty;
    public string BrowserCookies { get; init; } = string.Empty;
    public string AdditionalArguments { get; init; } = string.Empty;
    public string FilenameTemplate { get; init; } = "%(title).200B [%(id)s].%(ext)s";
    public TrimRange Trim { get; init; } = new();
}

public sealed record MediaFormat(
    string Id,
    string Extension,
    string Resolution,
    string VideoCodec,
    string AudioCodec,
    double? Bitrate,
    long? FileSize);

public sealed record MediaMetadata(
    string Url,
    string Title,
    string ThumbnailUrl,
    double? DurationSeconds,
    bool IsPlaylist,
    bool IsLive,
    IReadOnlyList<MediaFormat> Formats);
