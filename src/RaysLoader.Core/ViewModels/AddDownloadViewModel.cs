using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RaysLoader.Models;
using RaysLoader.Services;
using RaysLoader.Settings;

namespace RaysLoader.ViewModels;

public sealed partial class AddDownloadViewModel(IYtDlpService ytDlpService, ISettingsService settingsService) : ObservableObject, IDisposable
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
    [ObservableProperty] private AuthenticationMethodOption selectedAuthenticationMethod = AuthenticationOptions.FindMethod(AuthenticationMode.None);
    [ObservableProperty] private BrowserCookieOption selectedBrowser = AuthenticationOptions.FindBrowser(settingsService.Current.BrowserCookies);
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
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private string authenticationMessage = string.Empty;
    [ObservableProperty] private bool isAuthenticationExpanded;
    [ObservableProperty] private bool isBusy;
    private MediaMetadata? analyzedMetadata;
    private bool normalizingTrimRange;
    private CancellationTokenSource? analysisCancellation;
    private string analyzedUrlFingerprint = string.Empty;

    public ObservableCollection<MediaFormat> Formats { get; } = [];
    public ObservableCollection<MediaPreviewItem> Previews { get; } = [];
    public IReadOnlyList<DownloadMode> Modes { get; } = Enum.GetValues<DownloadMode>();
    public IReadOnlyList<string> VideoQualities { get; } = ["best", "2160p", "1440p", "1080p", "720p", "480p", "360p"];
    public IReadOnlyList<string> Containers { get; } = ["mp4", "mkv", "webm"];
    public IReadOnlyList<string> AudioFormats { get; } = ["mp3", "m4a", "opus", "wav"];
    public IReadOnlyList<string> AudioBitrates { get; } = ["320K", "256K", "192K", "128K", "96K"];
    public IReadOnlyList<AuthenticationMethodOption> AuthenticationMethods { get; } = AuthenticationOptions.Methods;
    public IReadOnlyList<BrowserCookieOption> Browsers { get; } = AuthenticationOptions.Browsers;
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
    public bool UsesBrowserAuthentication => SelectedAuthenticationMethod.Mode is AuthenticationMode.Browser;
    public bool UsesCookiesFile => SelectedAuthenticationMethod.Mode is AuthenticationMode.CookiesFile;
    public bool CanQueue => !IsBusy && Previews.Count > 0 && Previews.All(preview => preview.IsReady) &&
                            analyzedUrlFingerprint.Equals(CreateUrlFingerprint(Urls), StringComparison.Ordinal);
    public string PreviewStatus => IsBusy
        ? Previews.Count > 1 ? $"Reading {Previews.Count} media links..." : "Reading media details..."
        : Previews.Count == 0 ? "Paste a media link to begin." :
        Previews.All(preview => preview.IsReady) ?
            (Previews.Count == 1 ? "Preview ready" : $"{Previews.Count} previews ready") :
            "Some links could not be identified";
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

    partial void OnUrlsChanged(string value)
    {
        OnPropertyChanged(nameof(UrlInputSummary));
        ScheduleAutomaticAnalysis();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanQueue));
        OnPropertyChanged(nameof(PreviewStatus));
    }

    partial void OnSelectedAuthenticationMethodChanged(AuthenticationMethodOption value)
    {
        OnPropertyChanged(nameof(UsesBrowserAuthentication));
        OnPropertyChanged(nameof(UsesCookiesFile));
        AuthenticationMessage = string.Empty;
    }

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
    private Task AnalyzeAsync() => StartAnalysisAsync(debounce: false);

    private async Task StartAnalysisAsync(bool debounce)
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref analysisCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        if (debounce)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(550), cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
        }

        await AnalyzeUrlsAsync(cancellation);
    }

    private async Task AnalyzeUrlsAsync(CancellationTokenSource operation)
    {
        ErrorMessage = string.Empty;
        analyzedUrlFingerprint = string.Empty;
        Previews.Clear();
        OnPropertyChanged(nameof(CanQueue));
        OnPropertyChanged(nameof(PreviewStatus));
        IsBusy = true;
        try
        {
            var uris = UrlInputParser.Parse(Urls);
            ValidateAuthentication();
            foreach (var uri in uris) Previews.Add(new MediaPreviewItem(uri.AbsoluteUri));
            OnPropertyChanged(nameof(PreviewStatus));

            MediaMetadata? firstMetadata = null;
            foreach (var preview in Previews)
            {
                operation.Token.ThrowIfCancellationRequested();
                try
                {
                    var metadata = await ytDlpService.AnalyzeAsync(
                        BuildRequest(preview.Url),
                        operation.Token);
                    if (!ReferenceEquals(analysisCancellation, operation)) return;
                    firstMetadata ??= metadata;
                    preview.Title = metadata.Title;
                    preview.ThumbnailUrl = metadata.ThumbnailUrl;
                    preview.Source = metadata.ExtractorName.Length > 0 ? metadata.ExtractorName : "Detected media";
                    preview.Message = metadata.IsPlaylist ? "Playlist" : FormatDuration(metadata.DurationSeconds);
                    preview.State = MediaPreviewState.Ready;
                }
                catch (OperationCanceledException) when (operation.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    if (!ReferenceEquals(analysisCancellation, operation)) return;
                    preview.Title = "Preview unavailable";
                    preview.Source = GetSourceName(preview.Url);
                    preview.Message = string.IsNullOrWhiteSpace(exception.Message)
                        ? "Ray's Loader could not identify this link."
                        : exception.Message;
                    preview.State = MediaPreviewState.Failed;
                    HandleAuthenticationError(exception.Message);
                }
            }

            if (firstMetadata is not null) ApplyPrimaryMetadata(firstMetadata);
            if (Previews.Any(preview => preview.HasFailed))
            {
                ErrorMessage = Previews.Count == 1
                    ? Previews[0].Message
                    : $"{Previews.Count(preview => preview.HasFailed)} of {Previews.Count} links could not be identified. Review the previews below.";
            }
            else
            {
                analyzedUrlFingerprint = CreateUrlFingerprint(Urls);
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            if (ReferenceEquals(analysisCancellation, operation))
            {
                Previews.Clear();
                ResetTimeline();
            }
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(analysisCancellation, operation)) return;
            ErrorMessage = string.IsNullOrWhiteSpace(exception.Message)
                ? "Ray's Loader could not analyze this URL."
                : exception.Message;
            ResetTimeline();
            HandleAuthenticationError(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref analysisCancellation, null, operation), operation))
            {
                operation.Dispose();
                IsBusy = false;
                OnPropertyChanged(nameof(CanQueue));
                OnPropertyChanged(nameof(PreviewStatus));
            }
        }
    }

    public IReadOnlyList<DownloadRequest> CreateRequests()
    {
        ErrorMessage = string.Empty;
        ValidateAuthentication();
        var parsedUrls = UrlInputParser.Parse(Urls);
        if (!CanQueue) throw new FormatException("Wait for every media preview to finish before adding these links to the queue.");
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

    public MediaPreviewItem? FindPreview(string url) =>
        Previews.FirstOrDefault(preview => preview.Url.Equals(url, StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        var cancellation = Interlocked.Exchange(ref analysisCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
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
        CookiesFile = UsesCookiesFile ? CookiesFile : string.Empty,
        BrowserCookies = UsesBrowserAuthentication ? SelectedBrowser.Value : string.Empty,
        AdditionalArguments = AdditionalArguments,
        FilenameTemplate = settingsService.Current.FilenameTemplate,
        VideoFormatId = SelectedFormat is { VideoCodec.Length: > 0 } format && format.VideoCodec != "none" ? format.Id : string.Empty,
        AudioFormatId = SelectedFormat is { AudioCodec.Length: > 0 } audio && audio.AudioCodec != "none" && (audio.VideoCodec.Length == 0 || audio.VideoCodec == "none") ? audio.Id : string.Empty,
        Trim = new TrimRange(TrimEnabled, TrimStartSeconds, TrimEndSeconds, ExactBoundaries, SelectedClipMethod.Method),
    };

    private void ResetTimeline()
    {
        analyzedMetadata = null;
        Formats.Clear();
        SelectedFormat = null;
        MediaDurationSeconds = 0;
        TrimStartSeconds = 0;
        TrimEndSeconds = 0;
        CanSelectClip = false;
        TrimEnabled = false;
    }

    private void ScheduleAutomaticAnalysis()
    {
        analyzedUrlFingerprint = string.Empty;
        ErrorMessage = string.Empty;
        Previews.Clear();
        ResetTimeline();
        OnPropertyChanged(nameof(CanQueue));
        OnPropertyChanged(nameof(PreviewStatus));
        if (string.IsNullOrWhiteSpace(Urls))
        {
            var previous = Interlocked.Exchange(ref analysisCancellation, null);
            previous?.Cancel();
            previous?.Dispose();
            IsBusy = false;
            return;
        }
        IsBusy = true;
        _ = StartAnalysisAsync(debounce: true);
    }

    private void ApplyPrimaryMetadata(MediaMetadata metadata)
    {
        analyzedMetadata = metadata;
        MediaDurationSeconds = Math.Max(0, metadata.DurationSeconds ?? 0);
        TrimStartSeconds = 0;
        TrimEndSeconds = MediaDurationSeconds;
        CanSelectClip = Previews.Count == 1 && MediaDurationSeconds > 0 && !metadata.IsPlaylist && !metadata.IsLive;
        if (!CanSelectClip) TrimEnabled = false;
        Formats.Clear();
        foreach (var format in metadata.Formats) Formats.Add(format);
    }

    private void HandleAuthenticationError(string message)
    {
        if (!IsAuthenticationError(message)) return;
        IsAuthenticationExpanded = true;
        if (SelectedAuthenticationMethod.Mode is AuthenticationMode.None)
        {
            var preferredMode = settingsService.Current.AuthenticationMode is AuthenticationMode.None
                ? AuthenticationMode.Browser
                : settingsService.Current.AuthenticationMode;
            SelectedAuthenticationMethod = AuthenticationOptions.FindMethod(preferredMode);
        }
        AuthenticationMessage = "Sign in to this website with the selected browser, then retry the preview.";
    }

    public Uri GetSourceWebsiteUri()
    {
        var uri = UrlInputParser.Parse(Urls).First();
        return new Uri(uri.GetLeftPart(UriPartial.Authority));
    }

    private void ValidateAuthentication()
    {
        if (UsesCookiesFile)
        {
            if (string.IsNullOrWhiteSpace(CookiesFile))
                throw new FormatException("Choose a cookies.txt file before using cookie-file authentication.");
            if (!File.Exists(CookiesFile))
                throw new FormatException("The selected cookies file could not be found.");
        }
    }

    private static bool IsAuthenticationError(string message) =>
        message.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("sign in", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("log in", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("login", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("cookies", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("age restricted", StringComparison.OrdinalIgnoreCase);

    private static string CreateUrlFingerprint(string value)
    {
        try
        {
            return string.Join('\n', UrlInputParser.Parse(value).Select(uri => uri.AbsoluteUri));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static string GetSourceName(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase) : "Unknown source";

    private static string FormatDuration(double? seconds)
    {
        if (seconds is null or <= 0) return "Media ready";
        var duration = TimeSpan.FromSeconds(seconds.Value);
        return duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"m\:ss");
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
