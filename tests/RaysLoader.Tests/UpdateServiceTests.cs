using System.Net;
using System.Security.Cryptography;
using System.Text;
using RaysLoader.Services;

namespace RaysLoader.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RaysLoader.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FindsNewerVersionAndExpectedInstaller()
    {
        var bytes = Encoding.UTF8.GetBytes("verified installer");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        using var client = new HttpClient(new StubHandler(_ => JsonResponse(ReleaseJson("v1.2.0", bytes.Length, hash))));
        var service = new GitHubUpdateService(client, _directory, new Version(1, 1, 0));

        var update = await service.CheckForUpdateAsync();

        Assert.NotNull(update);
        Assert.Equal(new Version(1, 2, 0), update.Version);
        Assert.Equal("RaysLoader-Setup-1.2.0.exe", update.InstallerName);
        Assert.Equal(hash, update.Sha256);
    }

    [Fact]
    public async Task CurrentReleaseDoesNotOfferUpdate()
    {
        using var client = new HttpClient(new StubHandler(_ => JsonResponse(ReleaseJson("v1.1.0", 1, new string('A', 64)))));
        var service = new GitHubUpdateService(client, _directory, new Version(1, 1, 0));

        Assert.Null(await service.CheckForUpdateAsync());
        Assert.Null(service.AvailableUpdate);
    }

    [Fact]
    public async Task DownloadsInstallerOnlyAfterHashVerification()
    {
        var bytes = Encoding.UTF8.GetBytes("verified installer");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        using var client = new HttpClient(new StubHandler(request =>
            request.RequestUri?.AbsoluteUri == GitHubUpdateService.LatestReleaseUrl
                ? JsonResponse(ReleaseJson("v1.2.0", bytes.Length, hash))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        var service = new GitHubUpdateService(client, _directory, new Version(1, 1, 0));
        var update = await service.CheckForUpdateAsync();

        var path = await service.DownloadInstallerAsync(update!);

        Assert.True(File.Exists(path));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task RejectsInstallerWithWrongHash()
    {
        var bytes = Encoding.UTF8.GetBytes("tampered installer");
        using var client = new HttpClient(new StubHandler(request =>
            request.RequestUri?.AbsoluteUri == GitHubUpdateService.LatestReleaseUrl
                ? JsonResponse(ReleaseJson("v1.2.0", bytes.Length, new string('A', 64)))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        var service = new GitHubUpdateService(client, _directory, new Version(1, 1, 0));
        var update = await service.CheckForUpdateAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadInstallerAsync(update!));
        Assert.Empty(Directory.GetFiles(_directory, "*.exe"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static string ReleaseJson(string tag, int size, string hash) => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "Ray's Loader {{tag.TrimStart('v')}}",
          "html_url": "https://github.com/PKRReaperr/Rays-Loader/releases/tag/{{tag}}",
          "body": "Release notes",
          "assets": [
            {
              "name": "RaysLoader-Setup-{{tag.TrimStart('v')}}.exe",
              "state": "uploaded",
              "size": {{size}},
              "digest": "sha256:{{hash}}",
              "browser_download_url": "https://github.com/PKRReaperr/Rays-Loader/releases/download/{{tag}}/RaysLoader-Setup-{{tag.TrimStart('v')}}.exe"
            }
          ]
        }
        """;

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
