using System.Text;
using System.Text.Json;
using RaysLoader.Models;
using RaysLoader.ProcessManagement;
using RaysLoader.Settings;

namespace RaysLoader.Services;

public interface IYtDlpService
{
    Task<MediaMetadata> AnalyzeAsync(DownloadRequest request, CancellationToken cancellationToken = default);
    Task ExecuteDownloadAsync(DownloadTask task, CancellationToken cancellationToken = default);
}

public sealed class YtDlpService(
    IProcessRunnerService processRunner,
    IYtDlpArgumentBuilder argumentBuilder,
    IProgressParserService progressParser,
    IToolLocatorService toolLocator,
    IToolMaintenanceService toolMaintenanceService,
    ISettingsService settingsService,
    IUiDispatcher dispatcher) : IYtDlpService
{
    public async Task<MediaMetadata> AnalyzeAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        await toolMaintenanceService.WaitForIdleAsync(cancellationToken).ConfigureAwait(false);
        var tools = toolLocator.Locate(settingsService.Current);
        EnsureToolExists(tools.YtDlp, "yt-dlp");
        var standardOutput = new StringBuilder();
        var errors = new StringBuilder();
        var result = await processRunner.RunAsync(
            new ProcessRequest(
                tools.YtDlp,
                argumentBuilder.BuildProbeArguments(request.Url, request, tools.JavaScriptRuntime),
                Environment: YtDlpProcessEnvironment.Create(tools)),
            line =>
            {
                (line.IsError ? errors : standardOutput).AppendLine(line.Text);
                return ValueTask.CompletedTask;
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.WasCancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (result.ExitCode != 0)
        {
            throw new YtDlpException(ClassifyError(errors.ToString(), request));
        }

        try
        {
            using var document = JsonDocument.Parse(standardOutput.ToString());
            return ParseMetadata(request.Url, document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new YtDlpException("yt-dlp returned metadata that Ray's Loader could not read.", exception);
        }
    }

    public async Task ExecuteDownloadAsync(DownloadTask task, CancellationToken cancellationToken = default)
    {
        await toolMaintenanceService.WaitForIdleAsync(cancellationToken).ConfigureAwait(false);
        var settings = settingsService.Current;
        var tools = toolLocator.Locate(settings);
        EnsureToolExists(tools.YtDlp, "yt-dlp");
        if ((task.Request.Mode is not DownloadMode.VideoOnly || task.Request.Trim.Enabled) && !File.Exists(tools.Ffmpeg))
        {
            throw new MissingToolException("FFmpeg was not found. Open Settings > Tools and validate or select FFmpeg.");
        }

        dispatcher.Enqueue(() =>
        {
            task.State = DownloadState.Preparing;
            task.StatusText = "Preparing";
            task.CurrentOperation = "Starting yt-dlp...";
            task.StartedAt = DateTimeOffset.UtcNow;
            task.AppendLog("Starting yt-dlp and resolving the selected media streams.");
        });

        var lastProgressUpdate = DateTimeOffset.MinValue;
        using var activityCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var seekTimeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var downloadThenTrim = task.Request.Trim.Enabled && task.Request.Trim.Method is ClipDownloadMethod.DownloadThenTrim;
        var enforceSeekTimeout = task.Request.Trim.Enabled && !downloadThenTrim;
        if (enforceSeekTimeout)
        {
            seekTimeoutCancellation.CancelAfter(TimeSpan.FromMinutes(2));
        }
        var activityTask = task.Request.Trim.Enabled
            ? MonitorTrimActivityAsync(task, dispatcher, seekTimeoutCancellation, downloadThenTrim, activityCancellation.Token)
            : Task.CompletedTask;
        ProcessRunResult result;
        try
        {
            result = await processRunner.RunAsync(
                new ProcessRequest(
                    tools.YtDlp,
                    argumentBuilder.BuildDownloadArguments(task.Request, settings, tools.FfmpegDirectory, tools.JavaScriptRuntime),
                    Environment: YtDlpProcessEnvironment.Create(tools)),
                line =>
                {
                    dispatcher.Enqueue(() => task.AppendLog(line.Text, line.IsError, line.Timestamp));
                    if (line.Text.StartsWith("rays-title:", StringComparison.Ordinal))
                    {
                        dispatcher.Enqueue(() =>
                        {
                            task.Title = line.Text["rays-title:".Length..].Trim();
                            task.State = DownloadState.Downloading;
                            task.StatusText = downloadThenTrim ? "Downloading source" : task.Request.Trim.Enabled ? "Downloading clip" : "Downloading";
                            task.CurrentOperation = downloadThenTrim
                                ? "Downloading the full-quality source before trimming..."
                                : task.Request.Trim.Enabled ? "FFmpeg is seeking to the clip start..." : "Starting media transfer...";
                            if (task.Request.Trim.Enabled)
                            {
                                if (downloadThenTrim)
                                {
                                    task.AppendLog($"Full-quality clip mode is downloading the selected source streams first. FFmpeg will then keep {FormatDuration(task.Request.Trim.StartSeconds)} through {FormatDuration(task.Request.Trim.EndSeconds)} and discard the temporary source files.");
                                }
                                else
                                {
                                    task.AppendLog($"FFmpeg is seeking to {FormatDuration(task.Request.Trim.StartSeconds)} and will download {FormatDuration(task.Request.Trim.EndSeconds - task.Request.Trim.StartSeconds)}. Seeking in very long remote files can take several minutes.");
                                    if (task.Request.Trim.Method is ClipDownloadMethod.FastPartialDownload)
                                    {
                                        task.AppendLog("Fast partial mode is enabled. A lower-resolution combined stream may be used when it can seek more reliably.");
                                    }
                                }
                            }
                        });
                    }
                    else if (line.Text.StartsWith("rays-file:", StringComparison.Ordinal))
                    {
                        dispatcher.Enqueue(() => task.OutputFilename = line.Text["rays-file:".Length..].Trim());
                    }
                    else if (progressParser.TryParse(line.Text, out var progress))
                    {
                        seekTimeoutCancellation.CancelAfter(Timeout.InfiniteTimeSpan);
                        var now = DateTimeOffset.UtcNow;
                        if (now - lastProgressUpdate >= TimeSpan.FromMilliseconds(100) || progress.Percent >= 100 || progress.Operation != "Downloading")
                        {
                            lastProgressUpdate = now;
                            dispatcher.Enqueue(() => ApplyProgress(task, progress));
                        }
                    }

                    return ValueTask.CompletedTask;
                },
                processId => dispatcher.Enqueue(() => task.ProcessId = processId),
                seekTimeoutCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            activityCancellation.Cancel();
            try { await activityTask.ConfigureAwait(false); }
            catch (OperationCanceledException) when (activityCancellation.IsCancellationRequested) { }
        }

        if (result.WasCancelled)
        {
            if (enforceSeekTimeout && seekTimeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new YtDlpException("The source could not seek to the clip within 2 minutes. Retry with 'Fast partial download' or use 'Full quality, then trim'.");
            }

            dispatcher.Enqueue(() =>
            {
                task.State = DownloadState.Cancelled;
                task.StatusText = "Cancelled";
                task.ProcessId = null;
            });
            return;
        }

        if (result.ExitCode != 0)
        {
            throw new YtDlpException($"yt-dlp exited with code {result.ExitCode}. Open the task log for details.");
        }

        dispatcher.Enqueue(() =>
        {
            task.ProgressPercent = 100;
            task.State = DownloadState.Completed;
            task.StatusText = "Completed";
            task.CurrentOperation = string.Empty;
            task.ProcessId = null;
            task.CompletedAt = DateTimeOffset.UtcNow;
        });
    }

    private static void ApplyProgress(DownloadTask task, ProgressSnapshot progress)
    {
        if (progress.Percent is { } percent)
        {
            task.ProgressPercent = percent;
        }

        if (progress.Speed.Length > 0)
        {
            task.DownloadSpeed = progress.Speed;
        }

        if (progress.Eta.Length > 0)
        {
            task.Eta = progress.Eta;
        }

        if (progress.Operation.Length > 0)
        {
            task.CurrentOperation = progress.Operation;
            task.State = progress.Operation == "Downloading" ? DownloadState.Downloading : DownloadState.Processing;
            task.StatusText = progress.Operation;
        }
    }

    private static async Task MonitorTrimActivityAsync(
        DownloadTask task,
        IUiDispatcher dispatcher,
        CancellationTokenSource seekTimeoutCancellation,
        bool downloadThenTrim,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var nextLogAt = TimeSpan.FromSeconds(30);
        var announcedOutput = false;
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            var elapsed = DateTimeOffset.UtcNow - started;
            var receivedBytes = FindRecentPartialBytes(task.OutputDirectory, started.UtcDateTime);
            if (receivedBytes > 0)
            {
                seekTimeoutCancellation.CancelAfter(Timeout.InfiniteTimeSpan);
            }
            dispatcher.Enqueue(() =>
            {
                if (task.State is DownloadState.Processing or DownloadState.Completed or DownloadState.Failed or DownloadState.Cancelled) return;
                task.State = DownloadState.Downloading;
                task.StatusText = downloadThenTrim ? "Downloading source" : "Downloading clip";
                task.CurrentOperation = downloadThenTrim
                    ? receivedBytes > 0
                        ? $"Downloading full-quality source - {FormatBytes(receivedBytes)} received"
                        : $"Preparing full-quality source download - {elapsed:mm\\:ss} elapsed"
                    : receivedBytes > 0
                        ? $"Downloading selected section - {FormatBytes(receivedBytes)} received"
                        : $"FFmpeg is seeking to the clip start - {elapsed:mm\\:ss} elapsed";
                if (receivedBytes > 0 && !announcedOutput)
                {
                    announcedOutput = true;
                    task.AppendLog(downloadThenTrim
                        ? $"The full-quality source download started ({FormatBytes(receivedBytes)} received)."
                        : $"FFmpeg started writing the selected section ({FormatBytes(receivedBytes)} received).");
                }
                else if (elapsed >= nextLogAt)
                {
                    task.AppendLog(receivedBytes > 0
                        ? $"{(downloadThenTrim ? "Source" : "Clip")} transfer is active: {FormatBytes(receivedBytes)} received."
                        : downloadThenTrim
                            ? $"The full-quality source download is still preparing ({elapsed:mm\\:ss} elapsed)."
                            : $"FFmpeg is still seeking in the remote source ({elapsed:mm\\:ss} elapsed).");
                    nextLogAt += TimeSpan.FromSeconds(30);
                }
            });
        }
    }

    private static long FindRecentPartialBytes(string outputDirectory, DateTime startedUtc)
    {
        try
        {
            if (!Directory.Exists(outputDirectory)) return 0;
            return Directory.EnumerateFiles(outputDirectory, "*.part", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(file => file.LastWriteTimeUtc >= startedUtc.AddSeconds(-2))
                .Sum(file => file.Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return 0;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }

    private static string FormatDuration(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss\.fff");

    private static MediaMetadata ParseMetadata(string url, JsonElement root)
    {
        var isPlaylist = root.TryGetProperty("entries", out var entries) && entries.ValueKind is JsonValueKind.Array;
        var media = isPlaylist && entries.GetArrayLength() > 0 ? entries[0] : root;
        var formats = new List<MediaFormat>();
        if (media.ValueKind is not JsonValueKind.Undefined && media.TryGetProperty("formats", out var formatElements) && formatElements.ValueKind is JsonValueKind.Array)
        {
            foreach (var format in formatElements.EnumerateArray())
            {
                formats.Add(new MediaFormat(
                    ReadString(format, "format_id"),
                    ReadString(format, "ext"),
                    ReadString(format, "resolution"),
                    ReadString(format, "vcodec"),
                    ReadString(format, "acodec"),
                    ReadDouble(format, "tbr"),
                    ReadLong(format, "filesize") ?? ReadLong(format, "filesize_approx")));
            }
        }

        var liveStatus = ReadString(media, "live_status");
        var isLive = ReadBoolean(media, "is_live") || liveStatus is "is_live" or "is_upcoming";
        return new MediaMetadata(
            url,
            ReadString(root, "title").Length > 0 ? ReadString(root, "title") : ReadString(media, "title"),
            ReadString(media, "thumbnail"),
            ReadDouble(media, "duration"),
            isPlaylist,
            isLive,
            formats,
            ReadString(media, "extractor_key").Length > 0
                ? ReadString(media, "extractor_key")
                : ReadString(root, "extractor_key"));
    }

    internal static string ClassifyError(string error, DownloadRequest? request = null)
    {
        var normalized = error.ToLowerInvariant();
        if (IsFlickrUrl(request?.Url) && normalized.Contains("not a video"))
            return "This Flickr URL is a still photo. yt-dlp's Flickr extractor only downloads videos, so browser authentication cannot download this item.";
        if (normalized.Contains("unsupported url")) return "This website or URL is not supported by the installed yt-dlp version.";
        if (normalized.Contains("http error 401") || normalized.Contains("unauthorized"))
            return HasAuthentication(request)
                ? "The website rejected the supplied sign-in cookies (HTTP 401). Refresh the media page in the selected browser, confirm you can view it, and analyze again."
                : "The website requires authentication (HTTP 401). Open Authentication, sign in through your browser, and analyze again.";
        if (normalized.Contains("http error 403") || normalized.Contains("forbidden"))
            return HasAuthentication(request)
                ? "The website refused the authenticated request (HTTP 403). The browser cookies were supplied but were not accepted; refresh the media page and retry, or use a cookies.txt file."
                : "The website refused access (HTTP 403). Authentication, region restrictions, or anti-bot protection may be required.";
        if (normalized.Contains("sign in") || normalized.Contains("log in") || normalized.Contains("login") || normalized.Contains("cookies"))
            return "This media requires authentication. Open Authentication, sign in through your browser, and analyze again.";
        if (normalized.Contains("age")) return "This media appears to be age restricted. Authentication cookies may be required.";
        if (normalized.Contains("geo") || normalized.Contains("country")) return "This media is not available in the current region.";
        if (normalized.Contains("unable to download") || normalized.Contains("network")) return "The media website could not be reached. Check the network connection and try again.";
        return string.IsNullOrWhiteSpace(error) ? "yt-dlp could not analyze this URL." : error.Trim();
    }

    private static bool HasAuthentication(DownloadRequest? request) =>
        !string.IsNullOrWhiteSpace(request?.BrowserCookies) || !string.IsNullOrWhiteSpace(request?.CookiesFile);

    private static bool IsFlickrUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Host.Equals("flickr.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".flickr.com", StringComparison.OrdinalIgnoreCase));

    private static void EnsureToolExists(string path, string displayName)
    {
        if (!File.Exists(path))
        {
            throw new MissingToolException($"{displayName} was not found. Open Settings > Tools to select or install it.");
        }
    }

    private static string ReadString(JsonElement element, string name) => element.ValueKind is JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static bool ReadBoolean(JsonElement element, string name) => element.ValueKind is JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;
    private static double? ReadDouble(JsonElement element, string name) => element.ValueKind is JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.Number && value.TryGetDouble(out var result) ? result : null;
    private static long? ReadLong(JsonElement element, string name) => element.ValueKind is JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.Number && value.TryGetInt64(out var result) ? result : null;
}

public sealed class YtDlpException(string message, Exception? innerException = null) : Exception(message, innerException);
public sealed class MissingToolException(string message) : Exception(message);
