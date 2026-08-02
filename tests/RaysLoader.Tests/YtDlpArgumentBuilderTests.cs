using RaysLoader.Models;
using RaysLoader.Services;
using RaysLoader.Settings;

namespace RaysLoader.Tests;

public sealed class YtDlpArgumentBuilderTests
{
    private readonly YtDlpArgumentBuilder _builder = new();

    [Fact]
    public void BuildsStructuredVideoArgumentsWithPathsContainingSpaces()
    {
        var request = new DownloadRequest
        {
            Url = "https://example.test/watch?v=one&list=two",
            OutputDirectory = @"C:\Media Files\Ray's Loader",
            VideoQuality = "1080p",
            Container = "mkv",
            WriteSubtitles = true,
            EmbedMetadata = true,
        };

        var arguments = _builder.BuildDownloadArguments(request, new AppSettings(), @"C:\Program Files\RaysLoader\tools");

        Assert.Contains(@"C:\Media Files\Ray's Loader", arguments);
        Assert.Contains("--no-quiet", arguments);
        Assert.DoesNotContain("--progress-template", arguments);
        Assert.Contains("bestvideo[height<=1080]+bestaudio/best", arguments);
        Assert.Equal("mkv", arguments[arguments.IndexOf("--merge-output-format") + 1]);
        Assert.Contains("--write-subs", arguments);
        Assert.Contains("--embed-metadata", arguments);
        Assert.Equal(request.Url, arguments[^1]);
    }

    [Fact]
    public void BuildsTrimAndExactBoundaryArguments()
    {
        var request = new DownloadRequest
        {
            Url = "https://example.test/long",
            OutputDirectory = "C:\\Media",
            Trim = new TrimRange(true, 3723.25, 3731.25, true, ClipDownloadMethod.PartialDownload),
        };

        var arguments = _builder.BuildDownloadArguments(request, new AppSettings(), "C:\\Tools");

        Assert.Equal("*3723.25-3731.25", arguments[arguments.IndexOf("--download-sections") + 1]);
        Assert.Contains("--force-keyframes-at-cuts", arguments);
        Assert.Contains("[3723.25-3731.25]", arguments[arguments.IndexOf("-o") + 1]);
        Assert.Equal("bestvideo+bestaudio/best", arguments[arguments.IndexOf("-f") + 1]);
    }

    [Fact]
    public void TrimPrefersCombinedMediaAtTheSelectedQuality()
    {
        var request = new DownloadRequest
        {
            Url = "https://example.test/long",
            OutputDirectory = "C:\\Media",
            VideoQuality = "1080p",
            Trim = new TrimRange(true, 10, 18, false, ClipDownloadMethod.FastPartialDownload),
        };

        var arguments = _builder.BuildDownloadArguments(request, new AppSettings(), "C:\\Tools");

        Assert.Equal("best[height<=1080]/bestvideo[height<=1080]+bestaudio/best", arguments[arguments.IndexOf("-f") + 1]);
    }

    [Fact]
    public void TrimRespectsSelectedQualityByDefault()
    {
        var request = new DownloadRequest
        {
            Url = "https://example.test/long",
            OutputDirectory = "C:\\Media",
            VideoQuality = "2160p",
            Trim = new TrimRange(true, 10, 18, false),
        };

        var arguments = _builder.BuildDownloadArguments(request, new AppSettings(), "C:\\Tools");

        Assert.Equal("bestvideo[height<=2160]+bestaudio/best", arguments[arguments.IndexOf("-f") + 1]);
        Assert.DoesNotContain("--download-sections", arguments);
        Assert.Equal("Merger+ffmpeg_i:-ss 10 -t 8", arguments[arguments.IndexOf("--postprocessor-args") + 1]);
    }

    [Fact]
    public void DownloadThenTrimTargetsTheAudioExtractorForAudioOnlyMode()
    {
        var request = new DownloadRequest
        {
            Url = "https://example.test/long",
            OutputDirectory = "C:\\Media",
            Mode = DownloadMode.AudioOnly,
            Trim = new TrimRange(true, 20, 25),
        };

        var arguments = _builder.BuildDownloadArguments(request, new AppSettings(), "C:\\Tools");

        Assert.Equal("ExtractAudio+ffmpeg_i:-ss 20 -t 5", arguments[arguments.IndexOf("--postprocessor-args") + 1]);
        Assert.DoesNotContain("--download-sections", arguments);
    }

    [Fact]
    public void FullDownloadKeepsTheExistingDirectFormatSelector()
    {
        var request = new DownloadRequest
        {
            Url = "https://example.test/long",
            OutputDirectory = "C:\\Media",
            VideoQuality = "1080p",
        };

        var arguments = _builder.BuildDownloadArguments(request, new AppSettings(), "C:\\Tools");

        Assert.Equal("bestvideo[height<=1080]+bestaudio/best", arguments[arguments.IndexOf("-f") + 1]);
    }

    [Fact]
    public void KeepsAdvancedArgumentsSeparated()
    {
        var request = new DownloadRequest
        {
            Url = "https://example.test/video",
            OutputDirectory = "C:\\Media",
            AdditionalArguments = "--user-agent \"Ray Loader Test\" --no-mtime",
        };

        var arguments = _builder.BuildDownloadArguments(request, new AppSettings(), null);

        Assert.Equal("Ray Loader Test", arguments[arguments.IndexOf("--user-agent") + 1]);
        Assert.Contains("--no-mtime", arguments);
    }

    [Fact]
    public void AddsDetectedJavaScriptRuntimeToProbeAndDownload()
    {
        var request = new DownloadRequest
        {
            Url = "https://example.test/video",
            OutputDirectory = "C:\\Media",
        };
        const string runtime = @"node:C:\Program Files\nodejs\node.exe";

        var probe = _builder.BuildProbeArguments(request.Url, request, runtime);
        var download = _builder.BuildDownloadArguments(request, new AppSettings(), "C:\\Tools", runtime);

        Assert.Equal(runtime, probe[probe.IndexOf("--js-runtimes") + 1]);
        Assert.Equal(runtime, download[download.IndexOf("--js-runtimes") + 1]);
    }
}

internal static class ListExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] == value) return index;
        }

        return -1;
    }
}
