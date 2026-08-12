using RaysLoader.Models;
using RaysLoader.ProcessManagement;
using RaysLoader.Services;
using RaysLoader.Settings;

namespace RaysLoader.Tests;

public sealed class YtDlpServiceTests : IDisposable
{
    private readonly string _tool = Path.GetTempFileName();

    [Fact]
    public async Task AnalyzeAcceptsNullNumericFieldsInProbeJson()
    {
        const string json = """
            {
              "title": "Test media",
              "duration": 12.5,
              "thumbnail": "https://example.test/thumb.jpg",
              "extractor_key": "ExampleSite",
              "is_live": false,
              "formats": [
                { "format_id": "storyboard", "ext": "mhtml", "vcodec": "none", "acodec": "none", "tbr": null, "filesize": null },
                { "format_id": "18", "ext": "mp4", "resolution": "640x360", "vcodec": "h264", "acodec": "aac", "tbr": 500.25, "filesize_approx": 1234 }
              ]
            }
            """;
        var settings = new StubSettingsService();
        var service = new YtDlpService(
            new ProbeProcessRunner(json),
            new YtDlpArgumentBuilder(),
            new ProgressParserService(),
            new StubToolLocator(_tool),
            new StubToolMaintenanceService(),
            settings,
            new ImmediateUiDispatcher());

        var metadata = await service.AnalyzeAsync(new DownloadRequest
        {
            Url = "https://example.test/media",
            OutputDirectory = Path.GetTempPath(),
        });

        Assert.Equal("Test media", metadata.Title);
        Assert.Equal(12.5, metadata.DurationSeconds);
        Assert.Equal(2, metadata.Formats.Count);
        Assert.Null(metadata.Formats[0].Bitrate);
        Assert.Equal(1234, metadata.Formats[1].FileSize);
        Assert.Equal("ExampleSite", metadata.ExtractorName);
    }

    [Fact]
    public void FlickrStillPhotoErrorExplainsExtractorLimitation()
    {
        var request = new DownloadRequest
        {
            Url = "https://www.flickr.com/photos/example/123456789",
            OutputDirectory = Path.GetTempPath(),
            BrowserCookies = "firefox",
        };

        var message = YtDlpService.ClassifyError("ERROR: [Flickr] 123456789: not a video", request);

        Assert.Contains("still photo", message);
        Assert.Contains("only downloads videos", message);
        Assert.Contains("authentication cannot", message);
    }

    [Fact]
    public void AuthenticatedForbiddenErrorIsNotReportedAsNetworkFailure()
    {
        var request = new DownloadRequest
        {
            Url = "https://example.test/private",
            OutputDirectory = Path.GetTempPath(),
            BrowserCookies = "firefox",
        };

        var message = YtDlpService.ClassifyError("Unable to download webpage: HTTP Error 403: Forbidden", request);

        Assert.Contains("cookies were supplied", message);
        Assert.Contains("HTTP 403", message);
        Assert.DoesNotContain("network connection", message);
    }

    [Fact]
    public void UnauthorizedErrorDirectsUnauthenticatedUserToAuthentication()
    {
        var request = new DownloadRequest
        {
            Url = "https://example.test/private",
            OutputDirectory = Path.GetTempPath(),
        };

        var message = YtDlpService.ClassifyError("HTTP Error 401: Unauthorized", request);

        Assert.Contains("requires authentication", message);
        Assert.Contains("HTTP 401", message);
    }

    public void Dispose() => File.Delete(_tool);

    private sealed class ProbeProcessRunner(string json) : IProcessRunnerService
    {
        public async Task<ProcessRunResult> RunAsync(ProcessRequest request, Func<ProcessOutputLine, ValueTask> onOutput, Action<int>? onStarted = null, CancellationToken cancellationToken = default)
        {
            await onOutput(new ProcessOutputLine(json, false, DateTimeOffset.Now));
            return new ProcessRunResult(0, false);
        }
    }

    private sealed class StubToolLocator(string tool) : IToolLocatorService
    {
        public ToolPaths Locate(AppSettings settings) => new(tool, tool, tool);
    }

    private sealed class StubSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public string SettingsDirectory => Path.GetTempPath();
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubToolMaintenanceService : IToolMaintenanceService
    {
        public ToolHealthSnapshot? LastSnapshot => null;
        public string LastMessage => string.Empty;
        public Task<ToolHealthSnapshot> CheckStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<YtDlpMaintenanceResult> CheckAndUpdateYtDlpAsync(YtDlpUpdateChannel channel, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task WaitForIdleAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
