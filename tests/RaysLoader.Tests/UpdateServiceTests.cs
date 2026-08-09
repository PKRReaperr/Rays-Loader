using System.Net;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using RaysLoader.Services;

namespace RaysLoader.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RaysLoader.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FindsNewerVersionAndExactInstaller()
    {
        var bytes = Bytes("verified installer");
        using var service = CreateService(_ => JsonResponse(ReleaseJson("v1.2.0", bytes)));

        var update = await service.CheckForUpdateAsync();

        Assert.NotNull(update);
        Assert.Equal(V("1.2.0"), update.Version);
        Assert.Equal("RaysLoader-Setup-1.2.0.exe", update.InstallerName);
        Assert.Equal(Hash(bytes), update.Sha256);
    }

    [Fact]
    public async Task CurrentReleaseDoesNotOfferUpdate()
    {
        using var service = CreateService(_ => JsonResponse(ReleaseJson("v1.1.0", Bytes("same"))));

        Assert.Null(await service.CheckForUpdateAsync());
        Assert.Null(service.AvailableUpdate);
    }

    [Theory]
    [InlineData("1.2.0.0")]
    [InlineData("v1.2.0-beta.1")]
    [InlineData("release-1.2.0")]
    [InlineData("1.2")]
    public async Task RejectsUnsupportedReleaseTags(string tag)
    {
        using var service = CreateService(_ => JsonResponse(ReleaseJson(tag, Bytes("installer"))));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckForUpdateAsync());
    }

    [Fact]
    public async Task RejectsAmbiguousInstallerAssets()
    {
        var bytes = Bytes("installer");
        var asset = AssetJson("v1.2.0", bytes);
        var json = $$"""{"tag_name":"v1.2.0","name":"Release","html_url":"https://github.com/PKRReaperr/Rays-Loader/releases/tag/v1.2.0","body":"","assets":[{{asset}},{{asset}}]}""";
        using var service = CreateService(_ => JsonResponse(json));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckForUpdateAsync());
    }

    [Fact]
    public async Task RejectsMalformedReleaseDigest()
    {
        var bytes = Bytes("installer");
        var json = ReleaseJson("v1.2.0", bytes).Replace(Hash(bytes), "not-a-sha256", StringComparison.Ordinal);
        using var service = CreateService(_ => JsonResponse(json));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckForUpdateAsync());
    }

    [Fact]
    public async Task RejectsInstallerAssetFromAnotherGitHubRepository()
    {
        var bytes = Bytes("installer");
        var json = ReleaseJson("v1.2.0", bytes).Replace(
            "github.com/PKRReaperr/Rays-Loader/releases/download",
            "github.com/SomeoneElse/Other/releases/download",
            StringComparison.Ordinal);
        using var service = CreateService(_ => JsonResponse(json));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckForUpdateAsync());
    }

    [Fact]
    public async Task DownloadsInstallerOnlyAfterHashAndSignatureVerification()
    {
        var bytes = Bytes("verified installer");
        using var service = CreateDownloadService(bytes);
        var update = await service.CheckForUpdateAsync();

        var path = await service.DownloadInstallerAsync(update!);

        Assert.Equal(Path.Combine(_directory, update!.InstallerName), path);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.download"));
    }

    [Fact]
    public async Task RejectsInstallerWithWrongHash()
    {
        var bytes = Bytes("tampered installer");
        var advertised = Bytes("expected installer");
        using var service = CreateDownloadService(bytes, advertised);
        var update = await service.CheckForUpdateAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadInstallerAsync(update!));

        Assert.Empty(Directory.GetFiles(_directory, "*.exe"));
        Assert.Empty(Directory.GetFiles(_directory, "*.download"));
    }

    [Fact]
    public async Task DoesNotOverwriteUntrackedInstallerWithExpectedName()
    {
        var expected = Bytes("expected installer");
        var unrelated = Bytes("unrelated file");
        Directory.CreateDirectory(_directory);
        var update = CreateUpdate("1.2.0", expected);
        var path = Path.Combine(_directory, update.InstallerName);
        await File.WriteAllBytesAsync(path, unrelated);
        using var service = CreateDownloadService(expected);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadInstallerAsync(update));

        Assert.Contains("untracked file", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(unrelated, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task RejectsInvalidAuthenticodeSignature()
    {
        var bytes = Bytes("installer");
        using var service = CreateDownloadService(bytes, signature: new(InstallerSignatureStatus.Invalid, Message: "Bad signature"));
        var update = await service.CheckForUpdateAsync();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadInstallerAsync(update!));

        Assert.Contains("invalid Authenticode signature", exception.Message);
        Assert.Empty(Directory.GetFiles(_directory, "*.exe"));
    }

    [Fact]
    public async Task RequiresConfiguredTrustedPublisher()
    {
        var bytes = Bytes("installer");
        using var service = CreateDownloadService(
            bytes,
            signature: new(InstallerSignatureStatus.Valid, new string('B', 40)),
            security: new UpdateSecurityOptions(new string('A', 40)));
        var update = await service.CheckForUpdateAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadInstallerAsync(update!));
    }

    [Fact]
    public async Task AcceptsInstallerFromConfiguredTrustedPublisher()
    {
        var bytes = Bytes("installer");
        var thumbprint = new string('A', 40);
        using var service = CreateDownloadService(
            bytes,
            signature: new(InstallerSignatureStatus.Valid, thumbprint.ToLowerInvariant()),
            security: new UpdateSecurityOptions(thumbprint));
        var update = await service.CheckForUpdateAsync();

        var path = await service.DownloadInstallerAsync(update!);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void RejectsMalformedTrustedPublisherIdentity()
    {
        using var client = new HttpClient(new StubHandler(_ => JsonResponse("{}")));

        Assert.Throws<ArgumentException>(() => NewService(client, security: new UpdateSecurityOptions("1234")));
    }

    [Fact]
    public async Task RevalidatesTamperedInstallerImmediatelyBeforeLaunch()
    {
        var bytes = Bytes("verified installer");
        var launcher = new StubLauncher();
        using var service = CreateDownloadService(bytes, launcher: launcher);
        var update = await service.CheckForUpdateAsync();
        var path = await service.DownloadInstallerAsync(update!);
        await File.WriteAllBytesAsync(path, Bytes("tampered install!"));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.LaunchInstallerAsync(update!, path));

        Assert.Equal(0, launcher.LaunchCount);
    }

    [Fact]
    public async Task InstallerRemainsWriteLockedThroughLaunch()
    {
        var bytes = Bytes("verified installer");
        var launcher = new StubLauncher(path =>
            Assert.Throws<IOException>(() => File.WriteAllBytes(path, Bytes("tampered installer"))));
        using var service = CreateDownloadService(bytes, launcher: launcher);
        var update = await service.CheckForUpdateAsync();
        var path = await service.DownloadInstallerAsync(update!);

        await service.LaunchInstallerAsync(update!, path);

        Assert.Equal(1, launcher.LaunchCount);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("..\\RaysLoader-Setup-1.2.0.exe")]
    [InlineData("subdir/RaysLoader-Setup-1.2.0.exe")]
    [InlineData("C:\\RaysLoader-Setup-1.2.0.exe")]
    [InlineData("RaysLoader-Setup-1.2.0.msi")]
    [InlineData("RaysLoader-Setup-1.2.exe")]
    public async Task RejectsInvalidInstallerNames(string installerName)
    {
        var bytes = Bytes("installer");
        using var service = CreateDownloadService(bytes);
        var update = CreateUpdate("1.2.0", bytes) with { InstallerName = installerName };

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadInstallerAsync(update));
    }

    [Fact]
    public async Task RejectsLaunchPathOutsideManagedDirectory()
    {
        var bytes = Bytes("installer");
        var launcher = new StubLauncher();
        using var service = CreateDownloadService(bytes, launcher: launcher);
        var update = CreateUpdate("1.2.0", bytes);
        var outside = Path.Combine(Path.GetDirectoryName(_directory)!, update.InstallerName);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.LaunchInstallerAsync(update, outside));
        Assert.Equal(0, launcher.LaunchCount);
    }

    [Fact]
    public async Task RejectsMalformedLaunchPath()
    {
        var bytes = Bytes("installer");
        var launcher = new StubLauncher();
        using var service = CreateDownloadService(bytes, launcher: launcher);
        var update = CreateUpdate("1.2.0", bytes);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.LaunchInstallerAsync(update, "C:\\unsafe\0installer.exe"));
        Assert.Equal(0, launcher.LaunchCount);
    }

    [Fact]
    public async Task CancelsMetadataFetch()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new AsyncStubHandler(async (_, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }));
        using var service = NewService(client);
        using var cancellation = new CancellationTokenSource();
        var check = service.CheckForUpdateAsync(cancellation.Token);
        await started.Task;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
    }

    [Fact]
    public async Task CancellingDownloadRemovesPartialFileAndPreservesCurrentInstall()
    {
        var bytes = Bytes("first chunk");
        var stream = new FirstChunkThenBlockStream(bytes);
        using var client = new HttpClient(new AsyncStubHandler((request, _) => Task.FromResult(
            request.RequestUri?.AbsoluteUri == GitHubUpdateService.LatestReleaseUrl
                ? JsonResponse(ReleaseJson("v1.2.0", bytes, installerSize: bytes.Length + 100))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) })));
        using var service = NewService(client);
        var update = await service.CheckForUpdateAsync();
        using var cancellation = new CancellationTokenSource();
        var download = service.DownloadInstallerAsync(update!, cancellationToken: cancellation.Token);
        await stream.FirstChunkRead.Task;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.Empty(Directory.GetFiles(_directory, "*.download"));
        Assert.Empty(Directory.GetFiles(_directory, "*.exe"));
    }

    [Fact]
    public async Task CleanupRetainsTwoNewestManagedInstallersAndUnrelatedFiles()
    {
        Directory.CreateDirectory(_directory);
        var unrelatedInstaller = Path.Combine(_directory, "RaysLoader-Setup-9.9.9.exe");
        var unrelatedFile = Path.Combine(_directory, "keep-me.txt");
        await File.WriteAllTextAsync(unrelatedInstaller, "not tracked");
        await File.WriteAllTextAsync(unrelatedFile, "unrelated");
        using var service = CreateDownloadService(Bytes("unused"));
        var first = CreateUpdate("1.2.0", Bytes("one"));
        var second = CreateUpdate("1.3.0", Bytes("two"));
        var third = CreateUpdate("1.4.0", Bytes("three"));

        await DownloadDirectAsync(service, first, Bytes("one"));
        await DownloadDirectAsync(service, second, Bytes("two"));
        await DownloadDirectAsync(service, third, Bytes("three"));

        Assert.False(File.Exists(Path.Combine(_directory, first.InstallerName)));
        Assert.True(File.Exists(Path.Combine(_directory, second.InstallerName)));
        Assert.True(File.Exists(Path.Combine(_directory, third.InstallerName)));
        Assert.True(File.Exists(unrelatedInstaller));
        Assert.True(File.Exists(unrelatedFile));
    }

    [Fact]
    public async Task CleanupRemovesTrackedPartialAndPreservesUntrackedPartial()
    {
        Directory.CreateDirectory(_directory);
        var update = CreateUpdate("1.2.0", Bytes("partial"));
        var trackedPartial = Path.Combine(_directory, update.InstallerName + ".download");
        var unrelatedPartial = Path.Combine(_directory, "other.download");
        await File.WriteAllTextAsync(trackedPartial, "partial");
        await File.WriteAllTextAsync(unrelatedPartial, "unrelated");
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".raysloader-updates.json"),
            $$"""
            {
              "Artifacts": [
                {
                  "Name": "{{update.InstallerName}}",
                  "Version": "{{update.Version}}",
                  "Sha256": "{{update.Sha256}}",
                  "State": 0
                }
              ],
              "RollbackVersion": "1.1.0"
            }
            """);

        using var service = CreateDownloadService(Bytes("unused"));

        Assert.False(File.Exists(trackedPartial));
        Assert.True(File.Exists(unrelatedPartial));
        Assert.Equal(1, service.LastCleanupReport.DeletedArtifacts);
        Assert.Equal(V("1.1.0"), service.LastCleanupReport.RollbackVersion);
    }

    [Fact]
    public async Task CleanupReportsLockedManagedInstallerAndLeavesItTracked()
    {
        using var service = CreateDownloadService(Bytes("unused"));
        var first = CreateUpdate("1.2.0", Bytes("one"));
        var second = CreateUpdate("1.3.0", Bytes("two"));
        var third = CreateUpdate("1.4.0", Bytes("three"));
        await DownloadDirectAsync(service, first, Bytes("one"));
        await DownloadDirectAsync(service, second, Bytes("two"));
        await using var locked = new FileStream(Path.Combine(_directory, first.InstallerName), FileMode.Open, FileAccess.Read, FileShare.Read);

        await DownloadDirectAsync(service, third, Bytes("three"));

        Assert.False(service.LastCleanupReport.Succeeded);
        Assert.Contains(service.LastCleanupReport.Failures, failure => failure.FileName == first.InstallerName);
        Assert.True(File.Exists(Path.Combine(_directory, first.InstallerName)));
    }

    [Fact]
    public async Task LaunchRecordsRollbackVersionBeforeStartingInstaller()
    {
        var bytes = Bytes("installer");
        var launcher = new StubLauncher();
        using var service = CreateDownloadService(bytes, launcher: launcher);
        var update = await service.CheckForUpdateAsync();
        var path = await service.DownloadInstallerAsync(update!);

        await service.LaunchInstallerAsync(update!, path);
        var report = service.CleanupManagedArtifacts();

        Assert.Equal(V("1.1.0"), report.RollbackVersion);
        Assert.Equal(1, launcher.LaunchCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private GitHubUpdateService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) =>
        NewService(new HttpClient(new StubHandler(responseFactory)));

    private GitHubUpdateService CreateDownloadService(
        byte[] downloadedBytes,
        byte[]? advertisedBytes = null,
        InstallerSignatureResult? signature = null,
        UpdateSecurityOptions? security = null,
        StubLauncher? launcher = null)
    {
        advertisedBytes ??= downloadedBytes;
        var client = new HttpClient(new StubHandler(request =>
            request.RequestUri?.AbsoluteUri == GitHubUpdateService.LatestReleaseUrl
                ? JsonResponse(ReleaseJson("v1.2.0", advertisedBytes))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(downloadedBytes) }));
        return NewService(client, signature, security, launcher);
    }

    private GitHubUpdateService NewService(
        HttpClient client,
        InstallerSignatureResult? signature = null,
        UpdateSecurityOptions? security = null,
        StubLauncher? launcher = null) =>
        new(client, _directory, V("1.1.0"), new StubSignatureVerifier(signature ?? new(InstallerSignatureStatus.NotSigned)), launcher ?? new StubLauncher(), security);

    private async Task DownloadDirectAsync(GitHubUpdateService service, AppUpdate update, byte[] bytes)
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        using var direct = new GitHubUpdateService(client, _directory, V("1.1.0"), new StubSignatureVerifier(new(InstallerSignatureStatus.NotSigned)), new StubLauncher());
        await direct.DownloadInstallerAsync(update);
        _ = service.CleanupManagedArtifacts();
    }

    private static AppUpdate CreateUpdate(string version, byte[] bytes)
    {
        var parsed = V(version);
        var tag = "v" + parsed;
        return new AppUpdate(
            parsed,
            tag,
            $"Ray's Loader {parsed}",
            new Uri($"https://github.com/PKRReaperr/Rays-Loader/releases/tag/{tag}"),
            new Uri($"https://github.com/PKRReaperr/Rays-Loader/releases/download/{tag}/{parsed.InstallerFileName}"),
            parsed.InstallerFileName,
            bytes.Length,
            Hash(bytes),
            string.Empty);
    }

    private static ReleaseVersion V(string value) => ReleaseVersion.ParseTag(value);
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string ReleaseJson(string tag, byte[] bytes, int? installerSize = null) => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "Ray's Loader {{tag.TrimStart('v')}}",
          "html_url": "https://github.com/PKRReaperr/Rays-Loader/releases/tag/{{tag}}",
          "body": "Release notes",
          "assets": [{{AssetJson(tag, bytes, installerSize)}}]
        }
        """;

    private static string AssetJson(string tag, byte[] bytes, int? installerSize = null) => $$"""
        {
          "name": "RaysLoader-Setup-{{tag.TrimStart('v')}}.exe",
          "state": "uploaded",
          "size": {{installerSize ?? bytes.Length}},
          "digest": "sha256:{{Hash(bytes)}}",
          "browser_download_url": "https://github.com/PKRReaperr/Rays-Loader/releases/download/{{tag}}/RaysLoader-Setup-{{tag.TrimStart('v')}}.exe"
        }
        """;

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubSignatureVerifier(InstallerSignatureResult result) : IInstallerSignatureVerifier
    {
        public InstallerSignatureResult Verify(string installerPath) => result;
    }

    private sealed class StubLauncher(Action<string>? onLaunch = null) : IInstallerLauncher
    {
        public int LaunchCount { get; private set; }
        public void Launch(string installerPath)
        {
            onLaunch?.Invoke(installerPath);
            LaunchCount++;
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class AsyncStubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responseFactory(request, cancellationToken);
    }

    private sealed class FirstChunkThenBlockStream(byte[] firstChunk) : Stream
    {
        private bool sentFirstChunk;
        public TaskCompletionSource FirstChunkRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!sentFirstChunk)
            {
                sentFirstChunk = true;
                firstChunk.CopyTo(buffer);
                FirstChunkRead.SetResult();
                return firstChunk.Length;
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
