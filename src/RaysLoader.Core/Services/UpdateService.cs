using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace RaysLoader.Services;

public sealed record AppUpdate(
    Version Version,
    string Tag,
    string Name,
    Uri ReleasePage,
    Uri InstallerUrl,
    string InstallerName,
    long InstallerSize,
    string Sha256,
    string ReleaseNotes);

public interface IUpdateService
{
    Version CurrentVersion { get; }
    AppUpdate? AvailableUpdate { get; }
    Task<AppUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default);
    Task<string> DownloadInstallerAsync(AppUpdate update, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    void LaunchInstaller(string installerPath);
}

public sealed class GitHubUpdateService : IUpdateService
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/PKRReaperr/Rays-Loader/releases/latest";
    private readonly HttpClient _httpClient;
    private readonly string _updatesDirectory;

    public GitHubUpdateService(HttpClient httpClient, string updatesDirectory, Version? currentVersion = null)
    {
        _httpClient = httpClient;
        _updatesDirectory = updatesDirectory;
        CurrentVersion = currentVersion ?? typeof(GitHubUpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"RaysLoader/{CurrentVersion.ToString(3)}");
        }
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public Version CurrentVersion { get; }
    public AppUpdate? AvailableUpdate { get; private set; }

    public async Task<AppUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var release = await _httpClient.GetFromJsonAsync<GitHubRelease>(LatestReleaseUrl, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("GitHub returned an empty release response.");
        var version = ParseVersion(release.TagName);
        if (version <= CurrentVersion)
        {
            AvailableUpdate = null;
            return null;
        }

        var expectedName = $"RaysLoader-Setup-{version.ToString(3)}.exe";
        var asset = release.Assets.FirstOrDefault(candidate =>
            candidate.State.Equals("uploaded", StringComparison.OrdinalIgnoreCase) &&
            candidate.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Release {release.TagName} does not contain {expectedName}.");
        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var installerUri) ||
            installerUri.Scheme != Uri.UriSchemeHttps ||
            !installerUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The release installer URL is not a trusted GitHub HTTPS URL.");
        }

        var digest = asset.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) is true
            ? asset.Digest["sha256:".Length..]
            : string.Empty;
        if (digest.Length != 64 || !digest.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("The release installer does not include a valid SHA-256 digest.");
        }

        AvailableUpdate = new AppUpdate(
            version,
            release.TagName,
            string.IsNullOrWhiteSpace(release.Name) ? $"Ray's Loader {version.ToString(3)}" : release.Name,
            CreateTrustedReleaseUri(release.HtmlUrl),
            installerUri,
            asset.Name,
            asset.Size,
            digest.ToUpperInvariant(),
            release.Body ?? string.Empty);
        return AvailableUpdate;
    }

    public async Task<string> DownloadInstallerAsync(
        AppUpdate update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        Directory.CreateDirectory(_updatesDirectory);
        var finalPath = Path.Combine(_updatesDirectory, update.InstallerName);
        var partialPath = finalPath + ".download";

        try
        {
            if (File.Exists(finalPath) && await HasExpectedHashAsync(finalPath, update.Sha256, cancellationToken).ConfigureAwait(false))
            {
                progress?.Report(100);
                return finalPath;
            }

            using var response = await _httpClient.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long received = 0;
            {
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                var buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;
                    if (update.InstallerSize > 0) progress?.Report(Math.Min(100, received * 100d / update.InstallerSize));
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (update.InstallerSize > 0 && received != update.InstallerSize)
            {
                throw new InvalidDataException("The downloaded installer size does not match the GitHub release.");
            }
            if (!await HasExpectedHashAsync(partialPath, update.Sha256, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException("The downloaded installer failed SHA-256 verification.");
            }

            File.Move(partialPath, finalPath, overwrite: true);
            progress?.Report(100);
            return finalPath;
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    public void LaunchInstaller(string installerPath)
    {
        if (!File.Exists(installerPath)) throw new FileNotFoundException("The downloaded installer could not be found.", installerPath);
        Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
    }

    private static async Task<bool> HasExpectedHashAsync(string path, string expectedHash, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        return hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static Version ParseVersion(string tag)
    {
        var value = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(value, out var version)) throw new InvalidDataException($"The release tag '{tag}' is not a valid version.");
        return version.Build < 0 ? new Version(version.Major, version.Minor, 0) : version;
    }

    private static Uri CreateTrustedReleaseUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The release page is not a trusted GitHub HTTPS URL.");
        }
        return uri;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { Debug.WriteLine(exception); }
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset> Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("digest")] string? Digest,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
