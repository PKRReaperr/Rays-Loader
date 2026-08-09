using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using RaysLoader.Settings;

namespace RaysLoader.Services;

public sealed record AppUpdate(
    ReleaseVersion Version,
    string Tag,
    string Name,
    Uri ReleasePage,
    Uri InstallerUrl,
    string InstallerName,
    long InstallerSize,
    string Sha256,
    string ReleaseNotes);

public sealed record UpdateCleanupFailure(string FileName, string Message);

public sealed record UpdateCleanupReport(
    int DeletedArtifacts,
    IReadOnlyList<UpdateCleanupFailure> Failures,
    ReleaseVersion? RollbackVersion = null)
{
    public bool Succeeded => Failures.Count == 0;
    public static UpdateCleanupReport Empty { get; } = new(0, []);
}

public interface IUpdateService
{
    ReleaseVersion CurrentVersion { get; }
    AppUpdate? AvailableUpdate { get; }
    UpdateCleanupReport LastCleanupReport { get; }
    Task<AppUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default);
    Task<string> DownloadInstallerAsync(AppUpdate update, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task LaunchInstallerAsync(AppUpdate update, string installerPath, CancellationToken cancellationToken = default);
    UpdateCleanupReport CleanupManagedArtifacts();
    void CancelActiveOperations();
}

public sealed class GitHubUpdateService : IUpdateService, IDisposable
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/PKRReaperr/Rays-Loader/releases/latest";
    private const string RegistryFileName = ".raysloader-updates.json";
    private const int RetainedInstallerLimit = 2;
    private static readonly JsonSerializerOptions RegistryJsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;
    private readonly string _updatesDirectory;
    private readonly IInstallerSignatureVerifier _signatureVerifier;
    private readonly IInstallerLauncher _installerLauncher;
    private readonly UpdateSecurityOptions _securityOptions;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private bool _disposed;

    public GitHubUpdateService(
        HttpClient httpClient,
        string updatesDirectory,
        ReleaseVersion? currentVersion = null,
        IInstallerSignatureVerifier? signatureVerifier = null,
        IInstallerLauncher? installerLauncher = null,
        UpdateSecurityOptions? securityOptions = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (string.IsNullOrWhiteSpace(updatesDirectory)) throw new ArgumentException("The managed updates directory is required.", nameof(updatesDirectory));
        _httpClient = httpClient;
        _updatesDirectory = Path.GetFullPath(updatesDirectory);
        _signatureVerifier = signatureVerifier ?? new AuthenticodeInstallerSignatureVerifier();
        _installerLauncher = installerLauncher ?? new ShellInstallerLauncher();
        var configuredSecurity = securityOptions ?? UpdateSecurityOptions.FromAssembly(typeof(GitHubUpdateService).Assembly);
        var trustedThumbprint = UpdateSecurityOptions.NormalizeThumbprint(configuredSecurity.TrustedPublisherThumbprint);
        if (trustedThumbprint.Length is not (0 or 40))
            throw new ArgumentException("The trusted Authenticode publisher thumbprint must contain exactly 40 hexadecimal characters.", nameof(securityOptions));
        _securityOptions = new UpdateSecurityOptions(trustedThumbprint);
        CurrentVersion = currentVersion ?? ReleaseVersion.FromAssemblyVersion(
            typeof(GitHubUpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0));
        LastCleanupReport = CleanupManagedArtifacts();

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"RaysLoader/{CurrentVersion}");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public ReleaseVersion CurrentVersion { get; }
    public AppUpdate? AvailableUpdate { get; private set; }
    public UpdateCleanupReport LastCleanupReport { get; private set; }

    public async Task<AppUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CreateOperationCancellation(cancellationToken);
        await _operationGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        try
        {
            var release = await _httpClient.GetFromJsonAsync<GitHubRelease>(LatestReleaseUrl, linkedCancellation.Token).ConfigureAwait(false)
                ?? throw new InvalidDataException("GitHub returned an empty release response.");
            var version = ReleaseVersion.ParseTag(release.TagName);
            if (version <= CurrentVersion)
            {
                AvailableUpdate = null;
                return null;
            }

            var expectedName = version.InstallerFileName;
            var assets = release.Assets ?? throw new InvalidDataException("The GitHub release does not contain an asset list.");
            var matchingAssets = assets.Where(candidate =>
                string.Equals(candidate.State, "uploaded", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.Name, expectedName, StringComparison.Ordinal)).ToArray();
            if (matchingAssets.Length != 1)
                throw new InvalidDataException($"Release {release.TagName} must contain exactly one {expectedName} asset.");
            var asset = matchingAssets[0];
            var installerUri = CreateTrustedInstallerUri(asset.BrowserDownloadUrl, release.TagName, expectedName);
            var digest = NormalizeSha256(asset.Digest);
            if (asset.Size <= 0) throw new InvalidDataException("The release installer size is invalid.");

            AvailableUpdate = new AppUpdate(
                version,
                release.TagName,
                string.IsNullOrWhiteSpace(release.Name) ? $"Ray's Loader {version}" : release.Name,
                CreateTrustedReleaseUri(release.HtmlUrl, release.TagName),
                installerUri,
                expectedName,
                asset.Size,
                digest,
                release.Body ?? string.Empty);
            return AvailableUpdate;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<string> DownloadInstallerAsync(
        AppUpdate update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ValidateUpdate(update);
        using var linkedCancellation = CreateOperationCancellation(cancellationToken);
        await _operationGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        try
        {
            EnsureTrustedUpdatesDirectory();
            var finalPath = GetExpectedInstallerPath(update);
            var partialName = update.InstallerName + ".download";
            var partialPath = GetManagedPath(partialName);
            var registry = LoadRegistry();

            if (File.Exists(finalPath))
            {
                var isTracked = registry.Artifacts.Any(item =>
                    item.Name.Equals(update.InstallerName, StringComparison.OrdinalIgnoreCase) &&
                    item.State == ArtifactState.Complete);
                try
                {
                    await VerifyInstallerAsync(update, finalPath, linkedCancellation.Token).ConfigureAwait(false);
                    TrackCompleteArtifact(registry, update);
                    SaveRegistry(registry);
                    LastCleanupReport = CleanupManagedArtifactsCore(finalPath);
                    progress?.Report(100);
                    return finalPath;
                }
                catch (InvalidDataException) when (isTracked)
                {
                    // A stale or tampered managed installer is replaced only after a new
                    // partial download has been fully verified.
                }
                catch (InvalidDataException exception)
                {
                    throw new InvalidDataException(
                        $"An untracked file named {update.InstallerName} already exists in the managed updates directory and was not overwritten.",
                        exception);
                }
            }

            DeleteTrackedPartial(registry, update.InstallerName, partialPath);
            TrackPartialArtifact(registry, update);
            SaveRegistry(registry);

            try
            {
                using var response = await _httpClient.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, linkedCancellation.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                long received = 0;
                await using (var input = await response.Content.ReadAsStreamAsync(linkedCancellation.Token).ConfigureAwait(false))
                await using (var output = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await input.ReadAsync(buffer, linkedCancellation.Token).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), linkedCancellation.Token).ConfigureAwait(false);
                        received += read;
                        progress?.Report(Math.Min(100, received * 100d / update.InstallerSize));
                    }
                    await output.FlushAsync(linkedCancellation.Token).ConfigureAwait(false);
                }

                if (received != update.InstallerSize)
                    throw new InvalidDataException("The downloaded installer size does not match the GitHub release.");
                await VerifyInstallerAsync(update, partialPath, linkedCancellation.Token).ConfigureAwait(false);
                File.Move(partialPath, finalPath, overwrite: true);
                TrackCompleteArtifact(registry, update);
                SaveRegistry(registry);
                LastCleanupReport = CleanupManagedArtifactsCore(finalPath);
                progress?.Report(100);
                return finalPath;
            }
            catch
            {
                var failures = new List<UpdateCleanupFailure>();
                var cleanupFailure = TryDeleteFile(partialName, partialPath);
                if (cleanupFailure is null)
                    registry.Artifacts.RemoveAll(item => item.Name.Equals(update.InstallerName, StringComparison.OrdinalIgnoreCase) && item.State == ArtifactState.Partial);
                else
                    failures.Add(cleanupFailure);
                TrySaveRegistry(registry, failures);
                LastCleanupReport = new UpdateCleanupReport(0, failures, ParseRollbackVersion(registry.RollbackVersion));
                throw;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task LaunchInstallerAsync(AppUpdate update, string installerPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ValidateUpdate(update);
        using var linkedCancellation = CreateOperationCancellation(cancellationToken);
        await _operationGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        try
        {
            var expectedPath = GetExpectedInstallerPath(update);
            string normalizedInstallerPath;
            try
            {
                normalizedInstallerPath = Path.GetFullPath(installerPath);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new InvalidDataException("The installer launch path is malformed.", exception);
            }
            if (!normalizedInstallerPath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The installer path is outside the managed updates directory or has an unexpected filename.");

            // This verification intentionally happens immediately before Process.Start so
            // a file modified after download verification is never launched.
            var registry = LoadRegistry();
            registry.RollbackVersion = CurrentVersion.ToString();
            SaveRegistry(registry);
            await VerifyInstallerAsync(update, expectedPath, linkedCancellation.Token, () =>
            {
                linkedCancellation.Token.ThrowIfCancellationRequested();
                _installerLauncher.Launch(expectedPath);
            }).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public UpdateCleanupReport CleanupManagedArtifacts()
    {
        LastCleanupReport = CleanupManagedArtifactsCore();
        return LastCleanupReport;
    }

    public void CancelActiveOperations()
    {
        if (!_shutdownCancellation.IsCancellationRequested) _shutdownCancellation.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelActiveOperations();
        _shutdownCancellation.Dispose();
        _operationGate.Dispose();
    }

    private async Task VerifyInstallerAsync(
        AppUpdate update,
        string path,
        CancellationToken cancellationToken,
        Action? verifiedAction = null)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The downloaded installer could not be found.", path);
        var file = new FileInfo(path);
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The installer file cannot be a reparse point.");

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        if (stream.Length != update.InstallerSize) throw new InvalidDataException("The installer size does not match the release metadata.");
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        if (!hash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The installer failed SHA-256 verification.");

        var signature = _signatureVerifier.Verify(path);
        if (signature.Status == InstallerSignatureStatus.Invalid)
            throw new InvalidDataException($"The installer has an invalid Authenticode signature. {signature.Message}".Trim());
        if (_securityOptions.TrustedPublisherThumbprint.Length > 0 &&
            (signature.Status != InstallerSignatureStatus.Valid ||
             !UpdateSecurityOptions.NormalizeThumbprint(signature.SignerThumbprint).Equals(_securityOptions.TrustedPublisherThumbprint, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("The installer is not signed by the trusted Ray's Loader publisher certificate.");
        }
        verifiedAction?.Invoke();
    }

    private void ValidateUpdate(AppUpdate update)
    {
        var parsedTag = ReleaseVersion.ParseTag(update.Tag);
        if (parsedTag != update.Version || !update.InstallerName.Equals(update.Version.InstallerFileName, StringComparison.Ordinal) ||
            !PathTrust.IsPlainFileName(update.InstallerName) || !update.InstallerName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The update version, tag, and installer filename do not agree.");
        }
        if (update.InstallerSize <= 0) throw new InvalidDataException("The installer size is invalid.");
        _ = NormalizeSha256("sha256:" + update.Sha256);
        _ = CreateTrustedReleaseUri(update.ReleasePage.AbsoluteUri, update.Tag);
        _ = CreateTrustedInstallerUri(update.InstallerUrl.AbsoluteUri, update.Tag, update.InstallerName);
    }

    private string GetExpectedInstallerPath(AppUpdate update) => GetManagedPath(update.Version.InstallerFileName);

    private string GetManagedPath(string fileName)
    {
        if (!PathTrust.TryGetSafeChildPath(_updatesDirectory, fileName, out var path))
            throw new InvalidDataException("The managed update path is invalid or traverses a reparse point.");
        return path;
    }

    private void EnsureTrustedUpdatesDirectory()
    {
        if (!Directory.Exists(_updatesDirectory)) Directory.CreateDirectory(_updatesDirectory);
        if (!PathTrust.TryGetTrustedDirectory(_updatesDirectory, out var trusted) ||
            !trusted.Equals(_updatesDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The managed updates directory is not trusted.");
        }
    }

    private CancellationTokenSource CreateOperationCancellation(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCancellation.Token);
    }

    private UpdateCleanupReport CleanupManagedArtifactsCore(string? newlyVerifiedPath = null)
    {
        // Cleanup policy: only registry-tracked updater artifacts and the updater's
        // own temporary registry file are eligible. Keep the two newest verified
        // installers for bounded rollback, remove tracked partials, ignore every
        // unrelated file, and report every failed deletion or registry write.
        if (!Directory.Exists(_updatesDirectory)) return UpdateCleanupReport.Empty;
        var failures = new List<UpdateCleanupFailure>();
        var deleted = 0;
        try
        {
            EnsureTrustedUpdatesDirectory();
            var registry = LoadRegistry();
            var temporaryRegistryPath = GetManagedPath(RegistryFileName + ".tmp");
            if (File.Exists(temporaryRegistryPath))
            {
                var failure = TryDeleteFile(RegistryFileName + ".tmp", temporaryRegistryPath);
                if (failure is null) deleted++;
                else failures.Add(failure);
            }
            foreach (var partial in registry.Artifacts.Where(item => item.State == ArtifactState.Partial).ToArray())
            {
                var partialName = partial.Name + ".download";
                var path = GetManagedPath(partialName);
                var existed = File.Exists(path);
                var failure = TryDeleteFile(partialName, path);
                if (failure is null)
                {
                    if (existed) deleted++;
                    registry.Artifacts.Remove(partial);
                }
                else failures.Add(failure);
            }

            var complete = registry.Artifacts
                .Where(item => item.State == ArtifactState.Complete)
                .Select(item => (Item: item, Version: ReleaseVersion.ParseTag(item.Version)))
                .OrderByDescending(item => item.Version)
                .ToArray();
            var preserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(newlyVerifiedPath)) preserved.Add(Path.GetFullPath(newlyVerifiedPath));
            foreach (var entry in complete)
            {
                var path = GetManagedPath(entry.Item.Name);
                if (!File.Exists(path))
                {
                    registry.Artifacts.Remove(entry.Item);
                    continue;
                }
                if (preserved.Count < RetainedInstallerLimit)
                {
                    preserved.Add(path);
                    continue;
                }

                var failure = TryDeleteFile(entry.Item.Name, path);
                if (failure is null)
                {
                    deleted++;
                    registry.Artifacts.Remove(entry.Item);
                }
                else failures.Add(failure);
            }
            TrySaveRegistry(registry, failures);
            return new UpdateCleanupReport(deleted, failures, ParseRollbackVersion(registry.RollbackVersion));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            failures.Add(new UpdateCleanupFailure(RegistryFileName, exception.Message));
            return new UpdateCleanupReport(deleted, failures);
        }
    }

    private ManagedUpdateRegistry LoadRegistry()
    {
        var path = GetManagedPath(RegistryFileName);
        if (!File.Exists(path)) return new ManagedUpdateRegistry();
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The managed update registry cannot be a reparse point.");
        var registry = JsonSerializer.Deserialize<ManagedUpdateRegistry>(File.ReadAllText(path), RegistryJsonOptions)
            ?? throw new InvalidDataException("The managed update registry is empty.");
        if (registry.Artifacts is null) throw new InvalidDataException("The managed update registry has no artifact list.");
        var artifactNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in registry.Artifacts)
        {
            var version = ReleaseVersion.ParseTag(artifact.Version);
            if (!string.Equals(artifact.Name, version.InstallerFileName, StringComparison.Ordinal) || !PathTrust.IsPlainFileName(artifact.Name) ||
                artifact.Sha256 is null || artifact.Sha256.Length != 64 || !artifact.Sha256.All(Uri.IsHexDigit) ||
                !Enum.IsDefined(artifact.State) || !artifactNames.Add(artifact.Name))
            {
                throw new InvalidDataException("The managed update registry contains an invalid artifact entry.");
            }
        }
        if (!string.IsNullOrWhiteSpace(registry.RollbackVersion)) _ = ReleaseVersion.ParseTag(registry.RollbackVersion);
        return registry;
    }

    private void SaveRegistry(ManagedUpdateRegistry registry)
    {
        EnsureTrustedUpdatesDirectory();
        var path = GetManagedPath(RegistryFileName);
        var temporary = GetManagedPath(RegistryFileName + ".tmp");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The managed update registry cannot be a reparse point.");
        if (File.Exists(temporary) && (File.GetAttributes(temporary) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The managed update registry temporary file cannot be a reparse point.");
        File.WriteAllText(temporary, JsonSerializer.Serialize(registry, RegistryJsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    private void TrySaveRegistry(ManagedUpdateRegistry registry, List<UpdateCleanupFailure> failures)
    {
        try { SaveRegistry(registry); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            failures.Add(new UpdateCleanupFailure(RegistryFileName, exception.Message));
        }
    }

    private static void TrackPartialArtifact(ManagedUpdateRegistry registry, AppUpdate update)
    {
        registry.Artifacts.RemoveAll(item => item.Name.Equals(update.InstallerName, StringComparison.OrdinalIgnoreCase));
        registry.Artifacts.Add(new ManagedUpdateArtifact(update.InstallerName, update.Version.ToString(), update.Sha256, ArtifactState.Partial));
    }

    private static void TrackCompleteArtifact(ManagedUpdateRegistry registry, AppUpdate update)
    {
        registry.Artifacts.RemoveAll(item => item.Name.Equals(update.InstallerName, StringComparison.OrdinalIgnoreCase));
        registry.Artifacts.Add(new ManagedUpdateArtifact(update.InstallerName, update.Version.ToString(), update.Sha256, ArtifactState.Complete));
    }

    private void DeleteTrackedPartial(ManagedUpdateRegistry registry, string installerName, string partialPath)
    {
        var tracked = registry.Artifacts.Any(item => item.Name.Equals(installerName, StringComparison.OrdinalIgnoreCase) && item.State == ArtifactState.Partial);
        if (!File.Exists(partialPath)) return;
        if (!tracked) throw new InvalidDataException("An untracked partial installer already exists in the managed updates directory.");
        var failure = TryDeleteFile(installerName + ".download", partialPath);
        if (failure is not null) throw new IOException($"The previous partial installer could not be removed: {failure.Message}");
    }

    private static UpdateCleanupFailure? TryDeleteFile(string fileName, string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new UpdateCleanupFailure(fileName, exception.Message);
        }
    }

    private static ReleaseVersion? ParseRollbackVersion(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : ReleaseVersion.ParseTag(value);

    private static string NormalizeSha256(string? digest)
    {
        var value = digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) is true
            ? digest["sha256:".Length..]
            : string.Empty;
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new InvalidDataException("The release installer does not include a valid SHA-256 digest.");
        return value.ToUpperInvariant();
    }

    private static Uri CreateTrustedReleaseUri(string value, string tag)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !Uri.UnescapeDataString(uri.AbsolutePath).Equals($"/PKRReaperr/Rays-Loader/releases/tag/{tag}", StringComparison.Ordinal))
            throw new InvalidDataException("The release page is not a trusted GitHub HTTPS URL.");
        return uri;
    }

    private static Uri CreateTrustedInstallerUri(string value, string tag, string installerName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !Uri.UnescapeDataString(uri.AbsolutePath).Equals(
                $"/PKRReaperr/Rays-Loader/releases/download/{tag}/{installerName}",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The release installer URL is not a trusted GitHub release asset URL.");
        }
        return uri;
    }

    private sealed class ManagedUpdateRegistry
    {
        public List<ManagedUpdateArtifact> Artifacts { get; set; } = [];
        public string RollbackVersion { get; set; } = string.Empty;
    }

    private sealed record ManagedUpdateArtifact(string Name, string Version, string Sha256, ArtifactState State);
    private enum ArtifactState { Partial, Complete }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset>? Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("digest")] string? Digest,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
