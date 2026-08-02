using System.Globalization;
using RaysLoader.Models;
using RaysLoader.Settings;

namespace RaysLoader.Services;

public interface IYtDlpArgumentBuilder
{
    IReadOnlyList<string> BuildProbeArguments(string url, DownloadRequest? request = null, string? javaScriptRuntime = null);
    IReadOnlyList<string> BuildDownloadArguments(DownloadRequest request, AppSettings settings, string? ffmpegDirectory, string? javaScriptRuntime = null);
}

public sealed class YtDlpArgumentBuilder : IYtDlpArgumentBuilder
{
    public const string ProgressPrefix = "rays-progress:";
    public const string ProgressTemplate = ProgressPrefix + "%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s|%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.fragment_index)s|%(progress.fragment_count)s|%(playlist_index)s|%(playlist_count)s";

    public IReadOnlyList<string> BuildProbeArguments(string url, DownloadRequest? request = null, string? javaScriptRuntime = null)
    {
        var arguments = new List<string> { "--dump-single-json", "--no-warnings", "--skip-download" };
        AddJavaScriptRuntime(arguments, javaScriptRuntime);
        AddAuthentication(arguments, request);
        arguments.Add(url);
        return arguments;
    }

    public IReadOnlyList<string> BuildDownloadArguments(DownloadRequest request, AppSettings settings, string? ffmpegDirectory, string? javaScriptRuntime = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var arguments = new List<string>
        {
            "--newline",
            "--continue",
            "--part",
            // --print implicitly enables quiet mode in yt-dlp. Restore normal
            // command-line output so progress and extractor details are visible.
            "--no-quiet",
            "--print",
            "before_dl:rays-title:%(title)s",
            "--print",
            "after_move:rays-file:%(filepath)s",
            "-P",
            request.OutputDirectory,
            "-o",
            request.Trim.Enabled
                ? $"%(title).200B [%(id)s] [{FormatNumber(request.Trim.StartSeconds)}-{FormatNumber(request.Trim.EndSeconds)}].%(ext)s"
                : request.FilenameTemplate,
            "-f",
            BuildFormatSelector(request),
        };

        if (!string.IsNullOrWhiteSpace(ffmpegDirectory))
        {
            arguments.AddRange(["--ffmpeg-location", ffmpegDirectory]);
        }

        AddJavaScriptRuntime(arguments, javaScriptRuntime);

        if (!request.DownloadPlaylist)
        {
            arguments.Add("--no-playlist");
        }

        AddAuthentication(arguments, request);

        if (request.Trim.Enabled)
        {
            if (request.Trim.StartSeconds < 0 || request.Trim.EndSeconds <= request.Trim.StartSeconds)
            {
                throw new ArgumentException("The trim end must be later than the nonnegative trim start.", nameof(request));
            }

            if (request.Trim.Method is ClipDownloadMethod.DownloadThenTrim)
            {
                if (request.Mode is DownloadMode.VideoOnly)
                {
                    throw new ArgumentException("Full-quality download-then-trim requires Video or Audio Only mode.", nameof(request));
                }
                if (request.Trim.ExactBoundaries)
                {
                    throw new ArgumentException("Precise cuts require a partial-download clip method.", nameof(request));
                }
                var postProcessor = request.Mode is DownloadMode.AudioOnly ? "ExtractAudio" : "Merger";
                arguments.AddRange([
                    "--postprocessor-args",
                    $"{postProcessor}+ffmpeg_i:-ss {FormatNumber(request.Trim.StartSeconds)} -t {FormatNumber(request.Trim.EndSeconds - request.Trim.StartSeconds)}",
                ]);
            }
            else
            {
                arguments.AddRange([
                    "--download-sections",
                    $"*{FormatNumber(request.Trim.StartSeconds)}-{FormatNumber(request.Trim.EndSeconds)}",
                ]);
                if (request.Trim.ExactBoundaries)
                {
                    arguments.Add("--force-keyframes-at-cuts");
                }
            }
        }

        if (request.Mode is DownloadMode.AudioOnly)
        {
            arguments.AddRange(["-x", "--audio-format", request.AudioFormat, "--audio-quality", request.AudioBitrate]);
        }
        else if (request.Mode is DownloadMode.Video)
        {
            arguments.AddRange(["--merge-output-format", request.Container]);
        }

        if (request.WriteSubtitles)
        {
            arguments.AddRange(["--write-subs", "--write-auto-subs", "--sub-langs", request.SubtitleLanguages]);
        }

        if (request.WriteThumbnail)
        {
            arguments.Add("--write-thumbnail");
        }

        if (request.EmbedMetadata)
        {
            arguments.Add("--embed-metadata");
        }

        if (!string.IsNullOrWhiteSpace(settings.Proxy))
        {
            arguments.AddRange(["--proxy", settings.Proxy]);
        }

        if (!string.IsNullOrWhiteSpace(settings.RateLimit))
        {
            arguments.AddRange(["--limit-rate", settings.RateLimit]);
        }

        if (settings.ConcurrentFragments > 1)
        {
            arguments.AddRange(["--concurrent-fragments", settings.ConcurrentFragments.ToString(CultureInfo.InvariantCulture)]);
        }

        if (settings.PreserveTemporaryFiles)
        {
            arguments.Add("--keep-video");
        }

        if (settings.VerboseLogging)
        {
            arguments.Add("--verbose");
        }

        arguments.AddRange(ArgumentTokenizer.Tokenize(settings.AdditionalArguments));
        arguments.AddRange(ArgumentTokenizer.Tokenize(request.AdditionalArguments));
        arguments.Add(request.Url);
        return arguments;
    }

    private static string BuildFormatSelector(DownloadRequest request)
    {
        if (request.Mode is DownloadMode.VideoOnly)
        {
            return request.VideoFormatId.Length > 0 ? request.VideoFormatId : VideoQualitySelector(request.VideoQuality);
        }

        if (request.Mode is DownloadMode.AudioOnly)
        {
            if (request.AudioFormatId.Length > 0) return request.AudioFormatId;
            return request.Trim.Method is ClipDownloadMethod.FastPartialDownload ? "bestaudio[protocol^=m3u8]/bestaudio" : "bestaudio";
        }

        if (request.VideoFormatId.Length > 0 && request.AudioFormatId.Length > 0)
        {
            return $"{request.VideoFormatId}+{request.AudioFormatId}";
        }

        if (request.VideoFormatId.Length > 0)
        {
            return $"{request.VideoFormatId}+bestaudio/best";
        }

        if (request.AudioFormatId.Length > 0)
        {
            return $"{VideoQualitySelector(request.VideoQuality)}+{request.AudioFormatId}/best";
        }

        var regularSelector = $"{VideoQualitySelector(request.VideoQuality)}+bestaudio/best";
        if (!request.Trim.Enabled || request.Trim.Method is not ClipDownloadMethod.FastPartialDownload)
        {
            return regularSelector;
        }

        // A combined audio/video stream can seek directly using its media index.
        // Separate high-resolution streams on very long sources may otherwise
        // make FFmpeg scan many gigabytes before the selected range begins.
        var combinedSelector = request.VideoQuality.EndsWith('p') && int.TryParse(request.VideoQuality[..^1], out var height)
            ? $"best[height<={height}]"
            : "best";
        return $"{combinedSelector}/{regularSelector}";
    }

    private static string VideoQualitySelector(string quality)
    {
        if (quality.EndsWith('p') && int.TryParse(quality[..^1], out var height))
        {
            return $"bestvideo[height<={height}]";
        }

        return "bestvideo";
    }

    private static void AddAuthentication(List<string> arguments, DownloadRequest? request)
    {
        if (!string.IsNullOrWhiteSpace(request?.CookiesFile))
        {
            arguments.AddRange(["--cookies", request.CookiesFile]);
        }
        else if (!string.IsNullOrWhiteSpace(request?.BrowserCookies))
        {
            arguments.AddRange(["--cookies-from-browser", request.BrowserCookies]);
        }
    }

    private static void AddJavaScriptRuntime(List<string> arguments, string? javaScriptRuntime)
    {
        if (!string.IsNullOrWhiteSpace(javaScriptRuntime))
        {
            arguments.AddRange(["--js-runtimes", javaScriptRuntime]);
        }
    }

    private static string FormatNumber(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
