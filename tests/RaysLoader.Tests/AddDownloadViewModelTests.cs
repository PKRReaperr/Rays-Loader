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
    public async Task UrlSummaryCountsValidUniqueLines()
    {
        var viewModel = new AddDownloadViewModel(
            new StubYtDlpService(new MediaMetadata(string.Empty, string.Empty, string.Empty, null, false, false, [])),
            new StubSettingsService())
        {
            Urls = "https://example.test/one\nhttps://example.test/two\nhttps://example.test/one",
        };

        await viewModel.AnalyzeCommand.ExecuteAsync(null);

        Assert.Equal("2 URLs ready to queue", viewModel.UrlInputSummary);
        Assert.Equal(2, viewModel.CreateRequests().Count);
    }

    [Fact]
    public async Task PortableDownloadsPathResolvesForQueuedRequest()
    {
        var viewModel = new AddDownloadViewModel(
            new StubYtDlpService(new MediaMetadata(string.Empty, string.Empty, string.Empty, null, false, false, [])),
            new StubSettingsService())
        {
            Urls = "https://example.test/video",
        };

        await viewModel.AnalyzeCommand.ExecuteAsync(null);

        var request = Assert.Single(viewModel.CreateRequests());

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            request.OutputDirectory);
    }

    [Fact]
    public async Task BlankFolderDefaultsToCurrentUsersDownloads()
    {
        var settings = new StubSettingsService();
        settings.Current.DefaultOutputDirectory = string.Empty;
        var viewModel = new AddDownloadViewModel(
            new StubYtDlpService(new MediaMetadata(string.Empty, string.Empty, string.Empty, null, false, false, [])),
            settings)
        {
            Urls = "https://example.test/video",
        };

        await viewModel.AnalyzeCommand.ExecuteAsync(null);

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
        Assert.Equal("ExampleSite", Assert.Single(viewModel.Previews).Source);
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

    [Fact]
    public async Task BrowserAuthenticationIsAddedToQueuedRequest()
    {
        var settings = new StubSettingsService();
        settings.Current.AuthenticationMode = AuthenticationMode.Browser;
        settings.Current.BrowserCookies = "edge";
        var viewModel = new AddDownloadViewModel(
            new StubYtDlpService(new MediaMetadata(string.Empty, string.Empty, string.Empty, null, false, false, [])),
            settings)
        {
            Urls = "https://www.flickr.com/photos/example/123",
            SelectedAuthenticationMethod = AuthenticationOptions.FindMethod(AuthenticationMode.Browser),
        };

        await viewModel.AnalyzeCommand.ExecuteAsync(null);

        var request = Assert.Single(viewModel.CreateRequests());

        Assert.Equal("edge", request.BrowserCookies);
        Assert.Empty(request.CookiesFile);
    }

    [Fact]
    public async Task SavedAuthenticationPreferenceIsNotAppliedToOrdinaryDownloads()
    {
        var settings = new StubSettingsService();
        settings.Current.AuthenticationMode = AuthenticationMode.Browser;
        settings.Current.BrowserCookies = "firefox";
        var viewModel = new AddDownloadViewModel(
            new StubYtDlpService(new MediaMetadata(string.Empty, string.Empty, string.Empty, null, false, false, [])),
            settings)
        {
            Urls = "https://www.youtube.com/watch?v=jNQXAC9IVRw",
        };

        await viewModel.AnalyzeCommand.ExecuteAsync(null);

        var request = Assert.Single(viewModel.CreateRequests());

        Assert.False(viewModel.UsesBrowserAuthentication);
        Assert.Empty(request.BrowserCookies);
        Assert.Empty(request.CookiesFile);
    }

    [Fact]
    public async Task AuthenticationFailureOpensGuidedBrowserFlow()
    {
        var viewModel = new AddDownloadViewModel(new AuthenticationFailureService(), new StubSettingsService())
        {
            Urls = "https://www.flickr.com/photos/example/123",
        };

        await viewModel.AnalyzeCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsAuthenticationExpanded);
        Assert.True(viewModel.UsesBrowserAuthentication);
        Assert.Contains("Sign in", viewModel.AuthenticationMessage);
    }

    [Fact]
    public async Task AutomaticAnalysisBuildsPreviewAndUnlocksQueueActions()
    {
        var metadata = new MediaMetadata(
            "https://example.test/video",
            "Automatic preview",
            "https://example.test/thumbnail.jpg",
            42,
            false,
            false,
            [],
            "ExampleSite");
        var service = new SignalingYtDlpService(metadata);
        using var viewModel = new AddDownloadViewModel(service, new StubSettingsService())
        {
            Urls = metadata.Url,
        };

        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.CanQueue);

        service.Release.TrySetResult();
        await WaitUntilAsync(() => !viewModel.IsBusy);

        var preview = Assert.Single(viewModel.Previews);
        Assert.Equal("Automatic preview", preview.Title);
        Assert.Equal("https://example.test/thumbnail.jpg", preview.ThumbnailUrl);
        Assert.Equal("ExampleSite", preview.Source);
        Assert.True(viewModel.CanQueue);
    }

    [Fact]
    public async Task MultipleUrlsProduceSeparateMediaPreviews()
    {
        using var viewModel = new AddDownloadViewModel(new PerUrlYtDlpService(), new StubSettingsService())
        {
            Urls = "https://example.test/one\nhttps://example.test/two",
        };

        await viewModel.AnalyzeCommand.ExecuteAsync(null);

        Assert.Collection(
            viewModel.Previews,
            preview =>
            {
                Assert.Equal("Media one", preview.Title);
                Assert.Equal("https://example.test/one.jpg", preview.ThumbnailUrl);
            },
            preview =>
            {
                Assert.Equal("Media two", preview.Title);
                Assert.Equal("https://example.test/two.jpg", preview.ThumbnailUrl);
            });
        Assert.True(viewModel.CanQueue);
        Assert.Equal(2, viewModel.CreateRequests().Count);
    }

    [Fact]
    public async Task ChangingUrlsInvalidatesCompletedPreviewUntilReplacementFinishes()
    {
        var service = new SignalingYtDlpService(new MediaMetadata(
            "https://example.test/one", "First", string.Empty, null, false, false, []));
        using var viewModel = new AddDownloadViewModel(service, new StubSettingsService())
        {
            Urls = "https://example.test/one",
        };
        var firstAnalysis = viewModel.AnalyzeCommand.ExecuteAsync(null);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        service.Release.TrySetResult();
        await firstAnalysis;
        Assert.True(viewModel.CanQueue);

        viewModel.Urls = "https://example.test/two";

        Assert.False(viewModel.CanQueue);
        Assert.Empty(viewModel.Previews);
        Assert.True(viewModel.IsBusy);
    }

    [Fact]
    public async Task FailedPreviewKeepsQueueActionsDisabled()
    {
        using var viewModel = new AddDownloadViewModel(new AuthenticationFailureService(), new StubSettingsService())
        {
            Urls = "https://example.test/private",
        };

        await viewModel.AnalyzeCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(viewModel.Previews).HasFailed);
        Assert.False(viewModel.CanQueue);
        Assert.Throws<FormatException>(() => viewModel.CreateRequests());
    }

    [Fact]
    public void CookiesFileAuthenticationRequiresAnExistingFile()
    {
        var viewModel = new AddDownloadViewModel(
            new StubYtDlpService(new MediaMetadata(string.Empty, string.Empty, string.Empty, null, false, false, [])),
            new StubSettingsService())
        {
            Urls = "https://example.test/private",
            SelectedAuthenticationMethod = AuthenticationOptions.FindMethod(AuthenticationMode.CookiesFile),
            CookiesFile = "C:\\missing\\cookies.txt",
        };

        var error = Assert.Throws<FormatException>(() => viewModel.CreateRequests());

        Assert.Contains("could not be found", error.Message);
    }

    private sealed class StubYtDlpService(MediaMetadata metadata) : IYtDlpService
    {
        public Task<MediaMetadata> AnalyzeAsync(DownloadRequest request, CancellationToken cancellationToken = default) => Task.FromResult(metadata);
        public Task ExecuteDownloadAsync(DownloadTask task, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class AuthenticationFailureService : IYtDlpService
    {
        public Task<MediaMetadata> AnalyzeAsync(DownloadRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<MediaMetadata>(new YtDlpException("This media requires authentication. Open Authentication, sign in through your browser, and analyze again."));

        public Task ExecuteDownloadAsync(DownloadTask task, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class PerUrlYtDlpService : IYtDlpService
    {
        public Task<MediaMetadata> AnalyzeAsync(DownloadRequest request, CancellationToken cancellationToken = default)
        {
            var id = new Uri(request.Url).Segments.Last().Trim('/');
            return Task.FromResult(new MediaMetadata(
                request.Url,
                $"Media {id}",
                $"https://example.test/{id}.jpg",
                60,
                false,
                false,
                [],
                "ExampleSite"));
        }

        public Task ExecuteDownloadAsync(DownloadTask task, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SignalingYtDlpService(MediaMetadata metadata) : IYtDlpService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<MediaMetadata> AnalyzeAsync(DownloadRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return metadata with { Url = request.Url };
        }

        public Task ExecuteDownloadAsync(DownloadTask task, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition()) await Task.Delay(10, timeout.Token);
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
