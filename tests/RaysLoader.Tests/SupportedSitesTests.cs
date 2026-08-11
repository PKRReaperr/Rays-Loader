using RaysLoader.Models;
using RaysLoader.ProcessManagement;
using RaysLoader.Services;
using RaysLoader.Settings;
using RaysLoader.ViewModels;

namespace RaysLoader.Tests;

public sealed class SupportedSitesTests : IDisposable
{
    private readonly string toolPath = Path.GetTempFileName();

    [Fact]
    public void ParserNormalizesBrokenGenericAndVariantExtractors()
    {
        var sites = SupportedSitesService.ParseExtractors(
        [
            "youtube:tab",
            "generic",
            "20min (CURRENTLY BROKEN)",
            "youtube",
            "youtube",
            "  ",
        ]);

        Assert.Equal(4, sites.Count);
        Assert.Equal("20min", sites[0].ExtractorName);
        Assert.True(sites[0].IsKnownBroken);
        Assert.True(sites.Single(site => site.ExtractorName == "generic").IsGeneric);
        var variant = sites.Single(site => site.ExtractorName == "youtube:tab");
        Assert.Equal("youtube", variant.SiteName);
        Assert.Equal("Extractor variant: tab", variant.Detail);
    }

    [Fact]
    public async Task ServiceReadsVersionAndExtractorsThenCachesByTool()
    {
        var runner = new CatalogProcessRunner();
        var service = CreateService(runner);

        var first = await service.GetSupportedSitesAsync();
        var second = await service.GetSupportedSitesAsync();

        Assert.Same(first, second);
        Assert.Equal("2026.06.09", first.YtDlpVersion);
        Assert.Equal(3, first.Sites.Count);
        Assert.Equal(2, runner.CallCount);
        Assert.All(runner.Requests, request => Assert.Contains("--ignore-config", request.Arguments));

        var refreshed = await service.GetSupportedSitesAsync(forceRefresh: true);

        Assert.NotSame(first, refreshed);
        Assert.Equal(4, runner.CallCount);
    }

    [Fact]
    public async Task ServicePropagatesProcessCancellation()
    {
        var service = CreateService(new CancelledProcessRunner());

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetSupportedSitesAsync());
    }

    [Fact]
    public async Task ViewModelFiltersByExtractorAndDescription()
    {
        var catalog = new SupportedSitesCatalog("test-version",
        [
            new SupportedSite("vimeo", "vimeo", "Dedicated extractor", false, false),
            new SupportedSite("youtube:tab", "youtube", "Extractor variant: tab", false, false),
            new SupportedSite("generic", "generic", "Generic fallback for compatible webpages and embedded media", false, true),
        ]);
        using var viewModel = new SupportedSitesViewModel(new StubSupportedSitesService(catalog));

        await viewModel.LoadAsync();
        viewModel.SearchText = "youtube";

        var result = Assert.Single(viewModel.FilteredSites);
        Assert.Equal("youtube:tab", result.ExtractorName);
        Assert.Equal("1 of 3 extractors", viewModel.CountText);

        viewModel.SearchText = "embedded";
        Assert.Equal("generic", Assert.Single(viewModel.FilteredSites).ExtractorName);
    }

    [Fact]
    public void IssueSearchLinkTargetsYtDlpAndSortsOldestFirst()
    {
        var site = new SupportedSite("youtube:tab", "youtube", "Extractor variant: tab", true, false);

        var link = SupportedSiteLinks.CreateIssueSearch(site);
        var query = Uri.UnescapeDataString(link.Query);

        Assert.Equal("github.com", link.Host);
        Assert.Equal("/yt-dlp/yt-dlp/issues", link.AbsolutePath);
        Assert.Contains("is:issue", query);
        Assert.Contains("sort:created-asc", query);
        Assert.Contains("\"youtube\"", query);
    }

    public void Dispose() => File.Delete(toolPath);

    private SupportedSitesService CreateService(IProcessRunnerService runner) => new(
        runner,
        new StubToolLocator(toolPath),
        new StubSettingsService());

    private sealed class CatalogProcessRunner : IProcessRunnerService
    {
        public int CallCount { get; private set; }
        public List<ProcessRequest> Requests { get; } = [];

        public async Task<ProcessRunResult> RunAsync(
            ProcessRequest request,
            Func<ProcessOutputLine, ValueTask> onOutput,
            Action<int>? onStarted = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Requests.Add(request);
            var output = request.Arguments.Contains("--version")
                ? "2026.06.09"
                : "youtube\nyoutube:tab\ngeneric";
            foreach (var line in output.Split('\n'))
            {
                await onOutput(new ProcessOutputLine(line, false, DateTimeOffset.Now));
            }
            return new ProcessRunResult(0, false);
        }
    }

    private sealed class CancelledProcessRunner : IProcessRunnerService
    {
        public Task<ProcessRunResult> RunAsync(
            ProcessRequest request,
            Func<ProcessOutputLine, ValueTask> onOutput,
            Action<int>? onStarted = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessRunResult(-1, true));
    }

    private sealed class StubSupportedSitesService(SupportedSitesCatalog catalog) : ISupportedSitesService
    {
        public Task<SupportedSitesCatalog> GetSupportedSitesAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default) => Task.FromResult(catalog);
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
