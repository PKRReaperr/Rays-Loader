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
}
