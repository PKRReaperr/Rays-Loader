using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RaysLoader.Models;
using RaysLoader.Services;
using RaysLoader.Settings;

namespace RaysLoader.ViewModels;

public sealed partial class AddDownloadViewModel(IYtDlpService ytDlpService, ISettingsService settingsService) : ObservableObject
{
    [ObservableProperty] private string urls = string.Empty;
    [ObservableProperty] private string outputDirectory = settingsService.Current.DefaultOutputDirectory;
    [ObservableProperty] private DownloadMode mode = DownloadMode.Video;
    [ObservableProperty] private string videoQuality = NormalizeVideoQuality(settingsService.Current.DefaultVideoQuality);
    [ObservableProperty] private string container = settingsService.Current.DefaultContainer;
    [ObservableProperty] private string audioFormat = settingsService.Current.DefaultAudioFormat;
    [ObservableProperty] private string audioBitrate = settingsService.Current.DefaultAudioBitrate;
    [ObservableProperty] private bool writeSubtitles = settingsService.Current.WriteSubtitles;
    [ObservableProperty] private bool embedMetadata = settingsService.Current.EmbedMetadata;
    [ObservableProperty] private bool writeThumbnail = settingsService.Current.WriteThumbnail;
    [ObservableProperty] private bool downloadPlaylist = settingsService.Current.DownloadPlaylists;
    [ObservableProperty] private string cookiesFile = settingsService.Current.CookiesFile;
    [ObservableProperty] private string additionalArguments = string.Empty;
    [ObservableProperty] private MediaFormat? selectedFormat;
    [ObservableProperty] private bool trimEnabled;
    [ObservableProperty] private double trimStartSeconds;
    [ObservableProperty] private double trimEndSeconds;
    [ObservableProperty] private double mediaDurationSeconds;
    [ObservableProperty] private bool canSelectClip;
    [ObservableProperty] private bool exactBoundaries;
    [ObservableProperty] private ClipDownloadMethodOption selectedClipMethod = new("Full quality, then trim (reliable)", ClipDownloadMethod.DownloadThenTrim);
    [ObservableProperty] private string analysisTitle = "Not analyzed";
    [ObservableProperty] private string analysisSource = "Analyze a URL to detect its source";
    [ObservableProperty] private string analysisThumbnailUrl = string.Empty;
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private bool isBusy;
    private MediaMetadata? analyzedMetadata;
    private bool normalizingTrimRange;

    public ObservableCollection<MediaFormat> Formats { get; } = [];
    public IReadOnlyList<DownloadMode> Modes { get; } = Enum.GetValues<DownloadMode>();
    public IReadOnlyList<string> VideoQualities { get; } = ["best", "2160p", "1440p", "1080p", "720p", "480p", "360p"];
    public IReadOnlyList<string> Containers { get; } = ["mp4", "mkv", "webm"];
    public IReadOnlyList<string> AudioFormats { get; } = ["mp3", "m4a", "opus", "wav"];
    public IReadOnlyList<string> AudioBitrates { get; } = ["320K", "256K", "192K", "128K", "96K"];
    public IReadOnlyList<ClipDownloadMethodOption> ClipMethods { get; } =
    [
        new("Full quality, then trim (reliable)", ClipDownloadMethod.DownloadThenTrim),
        new("Partial download (source-dependent)", ClipDownloadMethod.PartialDownload),
        new("Fast partial download (may lower quality)", ClipDownloadMethod.FastPartialDownload),
    ];
    public string TrimStartDisplay => FormatTimelineTime(TrimStartSeconds);
    public string TrimEndDisplay => FormatTimelineTime(TrimEndSeconds);
    public string TrimLengthDisplay => FormatTimelineTime(Math.Max(0, TrimEndSeconds - TrimStartSeconds));
    public string MediaDurationDisplay => FormatTimelineTime(MediaDurationSeconds);
    public string UrlInputSummary
    {
        get
        {
            var lines = Urls.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0) return "Enter one URL per line.";
            var valid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var invalid = 0;
            foreach (var line in lines)
            {
                if (Uri.TryCreate(line, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") valid.Add(uri.AbsoluteUri);
                else invalid++;
            }
            var ready = valid.Count == 1 ? "1 URL ready to queue" : $"{valid.Count} URLs ready to queue";
            return invalid == 0 ? ready : $"{ready} | {invalid} invalid line{(invalid == 1 ? string.Empty : "s")}";
        }
    }

    partial void OnUrlsChanged(string value) => OnPropertyChanged(nameof(UrlInputSummary));

    partial void OnTrimStartSecondsChanged(double value)
    {
        if (!normalizingTrimRange)
        {
            var maximum = Math.Max(0, MediaDurationSeconds);
            var clamped = Math.Clamp(value, 0, maximum);
            if (TrimEndSeconds > 0) clamped = Math.Min(clamped, TrimEndSeconds);
            if (!clamped.Equals(value))
            {
                normalizingTrimRange = true;
                TrimStartSeconds = clamped;
                normalizingTrimRange = false;
            }
        }
        OnPropertyChanged(nameof(TrimStartDisplay));
        OnPropertyChanged(nameof(TrimLengthDisplay));
    }

    partial void OnTrimEndSecondsChanged(double value)
    {
        if (!normalizingTrimRange)
        {
            var clamped = Math.Clamp(value, TrimStartSeconds, Math.Max(TrimStartSeconds, MediaDurationSeconds));
            if (!clamped.Equals(value))
            {
                normalizingTrimRange = true;
                TrimEndSeconds = clamped;
                normalizingTrimRange = false;
            }
        }
        OnPropertyChanged(nameof(TrimEndDisplay));
        OnPropertyChanged(nameof(TrimLengthDisplay));
    }

    partial void OnMediaDurationSecondsChanged(double value)
    {
        if (TrimStartSeconds > value) TrimStartSeconds = value;
        if (TrimEndSeconds > value) TrimEndSeconds = value;
        OnPropertyChanged(nameof(MediaDurationDisplay));
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        ErrorMessage = string.Empty;
        AnalysisTitle = "Analyzing...";
        IsBusy = true;
        try
        {
            var uri = UrlInputParser.Parse(Urls).First();
            var metadata = await ytDlpService.AnalyzeAsync(BuildRequest(uri.AbsoluteUri));
            analyzedMetadata = metadata;
            AnalysisTitle = metadata.Title;
            AnalysisSource = metadata.ExtractorName.Length > 0
                ? $"Detected source: {metadata.ExtractorName}"
                : "Source detected by yt-dlp";
            AnalysisThumbnailUrl = metadata.ThumbnailUrl;
            MediaDurationSeconds = Math.Max(0, metadata.DurationSeconds ?? 0);
            TrimStartSeconds = 0;
            TrimEndSeconds = MediaDurationSeconds;
            CanSelectClip = MediaDurationSeconds > 0 && !metadata.IsPlaylist && !metadata.IsLive;
            if (!CanSelectClip) TrimEnabled = false;
            Formats.Clear();
            foreach (var format in metadata.Formats) Formats.Add(format);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Analysis was cancelled.";
            AnalysisTitle = "Analysis cancelled";
            AnalysisSource = "Source detection cancelled";
            AnalysisThumbnailUrl = string.Empty;
            ResetTimeline();
        }
        catch (Exception exception)
        {
            ErrorMessage = string.IsNullOrWhiteSpace(exception.Message)
                ? "Ray's Loader could not analyze this URL."
                : exception.Message;
            AnalysisTitle = "Analysis failed";
            AnalysisSource = "Source could not be detected";
            AnalysisThumbnailUrl = string.Empty;
            ResetTimeline();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public IReadOnlyList<DownloadRequest> CreateRequests()
    {
        ErrorMessage = string.Empty;
        var parsedUrls = UrlInputParser.Parse(Urls);
        if (TrimEnabled && parsedUrls.Count != 1) throw new FormatException("Clip trimming supports one URL at a time.");
        if (TrimEnabled && (TrimStartSeconds < 0 || TrimEndSeconds <= TrimStartSeconds)) throw new FormatException("The clip end must be later than the nonnegative start.");
        if (TrimEnabled && analyzedMetadata is null) throw new FormatException("Analyze the URL before enabling clip selection.");
        if (TrimEnabled && analyzedMetadata?.IsPlaylist is true) throw new FormatException("Clip trimming is not available for playlists.");
        if (TrimEnabled && analyzedMetadata?.IsLive is true) throw new FormatException("Clip trimming is not available for live streams.");
        if (TrimEnabled && analyzedMetadata?.DurationSeconds is { } duration && TrimEndSeconds > duration) throw new FormatException("The clip end cannot exceed the media duration.");
        if (TrimEnabled && SelectedClipMethod.Method is ClipDownloadMethod.DownloadThenTrim && Mode is DownloadMode.VideoOnly) throw new FormatException("Full-quality download-then-trim requires Video or Audio Only mode. Choose Partial Download for Video Only mode.");
        if (TrimEnabled && SelectedClipMethod.Method is ClipDownloadMethod.DownloadThenTrim && ExactBoundaries) throw new FormatException("Precise cuts require a partial-download clip method. Turn off Precise cuts or choose Partial Download.");
        return parsedUrls.Select(uri => BuildRequest(uri.AbsoluteUri)).ToArray();
    }

    private DownloadRequest BuildRequest(string url) => new()
    {
        Url = url,
        OutputDirectory = AppPaths.ResolveOutputDirectory(OutputDirectory),
        Mode = Mode,
        VideoQuality = VideoQuality.Equals("Best", StringComparison.OrdinalIgnoreCase) ? "best" : VideoQuality,
        Container = Container,
        AudioFormat = AudioFormat,
        AudioBitrate = AudioBitrate,
        WriteSubtitles = WriteSubtitles,
        EmbedMetadata = EmbedMetadata,
        WriteThumbnail = WriteThumbnail,
        DownloadPlaylist = DownloadPlaylist,
        CookiesFile = CookiesFile,
        AdditionalArguments = AdditionalArguments,
        FilenameTemplate = settingsService.Current.FilenameTemplate,
        VideoFormatId = SelectedFormat is { VideoCodec.Length: > 0 } format && format.VideoCodec != "none" ? format.Id : string.Empty,
        AudioFormatId = SelectedFormat is { AudioCodec.Length: > 0 } audio && audio.AudioCodec != "none" && (audio.VideoCodec.Length == 0 || audio.VideoCodec == "none") ? audio.Id : string.Empty,
        Trim = new TrimRange(TrimEnabled, TrimStartSeconds, TrimEndSeconds, ExactBoundaries, SelectedClipMethod.Method),
    };

    private void ResetTimeline()
    {
        analyzedMetadata = null;
        MediaDurationSeconds = 0;
        TrimStartSeconds = 0;
        TrimEndSeconds = 0;
        CanSelectClip = false;
        TrimEnabled = false;
    }

    private static string FormatTimelineTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss")
            : time.ToString(@"m\:ss");
    }

    private static string NormalizeVideoQuality(string? quality)
    {
        string[] choices = ["best", "2160p", "1440p", "1080p", "720p", "480p", "360p"];
        return choices.FirstOrDefault(choice => choice.Equals(quality, StringComparison.OrdinalIgnoreCase)) ?? "best";
    }
}

public sealed record ClipDownloadMethodOption(string Label, ClipDownloadMethod Method);
