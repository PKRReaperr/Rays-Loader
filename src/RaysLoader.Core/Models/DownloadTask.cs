using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RaysLoader.Models;

public sealed class DownloadTask : ObservableObject
{
    private const int MaxLogCharacters = 256 * 1024;
    private readonly StringBuilder _log = new();
    private string _title;
    private string _outputFilename = string.Empty;
    private string _thumbnailUrl = string.Empty;
    private string _thumbnailPath = string.Empty;
    private double _progressPercent;
    private string _downloadSpeed = string.Empty;
    private string _eta = string.Empty;
    private string _statusText = "Queued";
    private string _currentOperation = string.Empty;
    private DownloadState _state = DownloadState.Queued;
    private string _errorMessage = string.Empty;
    private string _logText = string.Empty;
    private int? _processId;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _completedAt;

    public DownloadTask(DownloadRequest request, string? title = null)
    {
        Request = request;
        Id = Guid.NewGuid();
        _title = string.IsNullOrWhiteSpace(title) ? request.Url : title;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; }
    public DownloadRequest Request { get; }
    public string Url => Request.Url;

    /// <summary>Short format label for lists, such as "MP4 1080p" or "MP3 192K".</summary>
    public string FormatSummary => Request.Mode is DownloadMode.AudioOnly
        ? $"{Request.AudioFormat.ToUpperInvariant()} {Request.AudioBitrate}".Trim()
        : $"{Request.Container.ToUpperInvariant()} {Request.VideoQuality}".Trim();

    /// <summary>The link's host without "www.", or an empty string when the URL is not absolute.</summary>
    public string SiteHost => Uri.TryCreate(Request.Url, UriKind.Absolute, out var uri)
        ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host).ToLowerInvariant()
        : string.Empty;
    public string OutputDirectory => Request.OutputDirectory;
    public DateTimeOffset CreatedAt { get; }

    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public string OutputFilename { get => _outputFilename; set => SetProperty(ref _outputFilename, value); }
    public string ThumbnailUrl { get => _thumbnailUrl; set => SetProperty(ref _thumbnailUrl, value); }
    public string ThumbnailPath { get => _thumbnailPath; set => SetProperty(ref _thumbnailPath, value); }
    public double ProgressPercent { get => _progressPercent; set { if (SetProperty(ref _progressPercent, Math.Clamp(value, 0, 100))) OnPropertyChanged(nameof(StatusSummary)); } }
    public string DownloadSpeed { get => _downloadSpeed; set { if (SetProperty(ref _downloadSpeed, value)) OnPropertyChanged(nameof(StatusSummary)); } }
    public string Eta { get => _eta; set { if (SetProperty(ref _eta, value)) OnPropertyChanged(nameof(StatusSummary)); } }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public string CurrentOperation { get => _currentOperation; set { if (SetProperty(ref _currentOperation, value)) OnPropertyChanged(nameof(SourceLine)); } }

    /// <summary>The site host, followed by the current operation when there is one.</summary>
    public string SourceLine => string.Join("  /  ", new[] { SiteHost, CurrentOperation.Trim() }.Where(part => part.Length > 0));
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public string LogText { get => _logText; private set => SetProperty(ref _logText, value); }
    public int? ProcessId { get => _processId; set => SetProperty(ref _processId, value); }
    public DateTimeOffset? StartedAt { get => _startedAt; set => SetProperty(ref _startedAt, value); }
    public DateTimeOffset? CompletedAt { get => _completedAt; set => SetProperty(ref _completedAt, value); }

    public DownloadState State
    {
        get => _state;
        set
        {
            if (!SetProperty(ref _state, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanPause));
            OnPropertyChanged(nameof(CanResume));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanRetry));
            OnPropertyChanged(nameof(CanOpenFile));
            OnPropertyChanged(nameof(CanOpenFolder));
            OnPropertyChanged(nameof(StatusSummary));
        }
    }

    // True pause is intentionally not exposed until stop-and-resume has been verified end-to-end.
    public bool CanPause => false;
    public bool CanResume => State is DownloadState.Paused;
    public bool CanStop => State is DownloadState.Queued or DownloadState.Preparing or DownloadState.Analyzing or DownloadState.Downloading or DownloadState.Processing;
    public bool CanRetry => State is DownloadState.Failed or DownloadState.Cancelled;
    public bool CanOpenFile => State is DownloadState.Completed && !string.IsNullOrWhiteSpace(OutputFilename);
    public bool CanOpenFolder => State is DownloadState.Completed or DownloadState.Failed or DownloadState.Cancelled;
    public string StatusSummary
    {
        get
        {
            var parts = new List<string>();
            if (State is DownloadState.Downloading or DownloadState.Processing or DownloadState.Completed)
            {
                parts.Add($"{ProgressPercent:0.##}% Complete");
            }
            else
            {
                parts.Add(StatusText);
            }

            if (!string.IsNullOrWhiteSpace(DownloadSpeed)) parts.Add(DownloadSpeed);
            if (!string.IsNullOrWhiteSpace(Eta)) parts.Add($"{Eta} Remaining");
            return string.Join("  •  ", parts);
        }
    }

    public void AppendLog(string message, bool isError = false, DateTimeOffset? timestamp = null)
    {
        var trimmed = message.TrimStart();
        var prefix = trimmed.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase)
            ? "WARN"
            : trimmed.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase) ? "ERROR" : "INFO";
        _log.Append('[').Append((timestamp ?? DateTimeOffset.Now).ToString("HH:mm:ss")).Append("] ")
            .Append(prefix).Append("  ").AppendLine(message);
        if (_log.Length > MaxLogCharacters)
        {
            _log.Remove(0, _log.Length - (MaxLogCharacters / 2));
            _log.Insert(0, "[Log truncated to protect memory]" + Environment.NewLine);
        }

        LogText = _log.ToString();
    }
}
