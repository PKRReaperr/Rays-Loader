using RaysLoader.Models;
using RaysLoader.Services;
using RaysLoader.Settings;
using RaysLoader.ViewModels;

namespace RaysLoader.Tests;

public sealed class AddDownloadViewModelTests
{
    [Fact]
    public void EmptySavedVideoQualityDefaultsToBest()
    {
        var settings = new StubSettingsService();
        settings.Current.DefaultVideoQuality = string.Empty;

        var viewModel = new AddDownloadViewModel(
            new StubYtDlpService(new MediaMetadata(string.Empty, string.Empty, string.Empty, null, false, false, [])),
            settings);

        Assert.Equal("best", viewModel.VideoQuality);
        Assert.Contains(viewModel.VideoQuality, viewModel.VideoQualities);
    }

    [Fact]
    public void UrlSummaryCountsValidUniqueLines()
    {
        var viewModel = new AddDownloadViewModel(
            new StubYtDlpService(new MediaMetadata(string.Empty, string.Empty, string.Empty, null, false, false, [])),
            new StubSettingsService())
        {
            Urls = "https://example.test/one\nhttps://example.test/two\nhttps://example.test/one",
        };

        Assert.Equal("2 URLs ready to queue", viewModel.UrlInputSummary);
        Assert.Equal(2, viewModel.CreateRequests().Count);
    }

    [Fact]
    public void PortableDownloadsPathResolvesForQueuedRequest()
    {
        var viewModel = new AddDownloadViewModel(
            new StubYtDlpService(new MediaMetadata(string.Empty, string.Empty, string.Empty, null, false, false, [])),
            new StubSettingsService())
        {
            Urls = "https://example.test/video",
        };

        var request = Assert.Single(viewModel.CreateRequests());

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            request.OutputDirectory);
    }

    [Fact]
    public void BlankFolderDefaultsToCurrentUsersDownloads()
    {
        var settings = new StubSettingsService();
        settings.Current.DefaultOutputDirectory = string.Empty;
        var viewModel = new AddDownloadViewModel(
            new StubYtDlpService(new MediaMetadata(string.Empty, string.Empty, string.Empty, null, false, false, [])),
            settings)
        {
            Urls = "https://example.test/video",
        };

        var request = Assert.Single(viewModel.CreateRequests());

        Assert.Equal(AppPaths.DefaultOutputDirectory, request.OutputDirectory);
    }

    [Fact]
    public async Task AnalysisInitializesTimelineToFullMediaDuration()
    {
        var metadata = new MediaMetadata(
            "https://example.test/video",
            "Timeline test",
            string.Empty,
            3723.5,
            false,
            false,
            [],
            "ExampleSite");
        var viewModel = new AddDownloadViewModel(new StubYtDlpService(metadata), new StubSettingsService())
        {
            Urls = metadata.Url,
        };

        await viewModel.AnalyzeCommand.ExecuteAsync(null);

        Assert.True(viewModel.CanSelectClip);
        Assert.Equal(0, viewModel.TrimStartSeconds);
        Assert.Equal(3723.5, viewModel.TrimEndSeconds);
        Assert.Equal("0:00", viewModel.TrimStartDisplay);
        Assert.Equal("1:02:03", viewModel.TrimEndDisplay);
        Assert.Equal("1:02:03", viewModel.TrimLengthDisplay);
        Assert.Equal("1:02:03", viewModel.MediaDurationDisplay);
        Assert.Equal("Detected source: ExampleSite", viewModel.AnalysisSource);
    }

    [Fact]
    public async Task FineTuneValuesStayInsideTimelineAndDoNotCross()
    {
        var metadata = new MediaMetadata("https://example.test/video", "Test", string.Empty, 120, false, false, []);
        var viewModel = new AddDownloadViewModel(new StubYtDlpService(metadata), new StubSettingsService())
        {
            Urls = metadata.Url,
        };
        await viewModel.AnalyzeCommand.ExecuteAsync(null);

        viewModel.TrimStartSeconds = 30;
        viewModel.TrimEndSeconds = 90;
        viewModel.TrimStartSeconds = 100;
        viewModel.TrimEndSeconds = -10;

        Assert.Equal(90, viewModel.TrimStartSeconds);
        Assert.Equal(90, viewModel.TrimEndSeconds);
        Assert.Equal("0:00", viewModel.TrimLengthDisplay);
    }

    private sealed class StubYtDlpService(MediaMetadata metadata) : IYtDlpService
    {
        public Task<MediaMetadata> AnalyzeAsync(DownloadRequest request, CancellationToken cancellationToken = default) => Task.FromResult(metadata);
        public Task ExecuteDownloadAsync(DownloadTask task, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new() { DefaultOutputDirectory = AppPaths.UserDownloadsDirectory };
        public string SettingsDirectory => Path.GetTempPath();
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
