using System.Text;
using RaysLoader.Models;
using RaysLoader.ProcessManagement;

namespace RaysLoader.Services;

public interface ISupportedSitesService
{
    Task<SupportedSitesCatalog> GetSupportedSitesAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}

public sealed class SupportedSitesService(
    IProcessRunnerService processRunner,
    IToolLocatorService toolLocator,
    ISettingsService settingsService) : ISupportedSitesService
{
    private const string BrokenSuffix = " (CURRENTLY BROKEN)";
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private SupportedSitesCatalog? cachedCatalog;
    private string cachedToolFingerprint = string.Empty;

    public async Task<SupportedSitesCatalog> GetSupportedSitesAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var tools = toolLocator.Locate(settingsService.Current);
        if (!File.Exists(tools.YtDlp))
        {
            throw new MissingToolException("yt-dlp was not found. Open Settings > Tools to select or install it.");
        }

        var fingerprint = CreateToolFingerprint(tools.YtDlp);
        if (!forceRefresh && cachedCatalog is not null && cachedToolFingerprint == fingerprint)
        {
            return cachedCatalog;
        }

        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && cachedCatalog is not null && cachedToolFingerprint == fingerprint)
            {
                return cachedCatalog;
            }

            var versionOutput = await RunYtDlpAsync(
                tools,
                ["--ignore-config", "--version"],
                cancellationToken).ConfigureAwait(false);
            var extractorOutput = await RunYtDlpAsync(
                tools,
                ["--ignore-config", "--list-extractors"],
                cancellationToken).ConfigureAwait(false);

            var version = versionOutput
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? "Unknown";
            var sites = ParseExtractors(extractorOutput.Split(new[] { '\r', '\n' }));
            if (sites.Count == 0)
            {
                throw new YtDlpException("yt-dlp returned an empty supported-sites list.");
            }

            cachedCatalog = new SupportedSitesCatalog(version, sites);
            cachedToolFingerprint = fingerprint;
            return cachedCatalog;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    internal static IReadOnlyList<SupportedSite> ParseExtractors(IEnumerable<string> lines)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sites = new List<SupportedSite>();
        foreach (var rawLine in lines)
        {
            var value = rawLine.Trim();
            if (value.Length == 0) continue;

            var isKnownBroken = value.EndsWith(BrokenSuffix, StringComparison.Ordinal);
            var extractorName = isKnownBroken ? value[..^BrokenSuffix.Length].TrimEnd() : value;
            if (extractorName.Length == 0 || !seen.Add(extractorName)) continue;

            var separator = extractorName.IndexOf(':');
            var siteName = separator > 0 ? extractorName[..separator] : extractorName;
            var isGeneric = extractorName.Equals("generic", StringComparison.OrdinalIgnoreCase);
            var detail = isGeneric
                ? "Generic fallback for compatible webpages and embedded media"
                : separator > 0
                    ? $"Extractor variant: {extractorName[(separator + 1)..].Replace(":", " / ", StringComparison.Ordinal)}"
                    : "Dedicated extractor";

            sites.Add(new SupportedSite(extractorName, siteName, detail, isKnownBroken, isGeneric));
        }

        return sites
            .OrderBy(site => site.ExtractorName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<string> RunYtDlpAsync(
        ToolPaths tools,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var standardOutput = new StringBuilder();
        var errors = new StringBuilder();
        var result = await processRunner.RunAsync(
            new ProcessRequest(
                tools.YtDlp,
                arguments,
                Environment: YtDlpProcessEnvironment.Create(tools)),
            line =>
            {
                (line.IsError ? errors : standardOutput).AppendLine(line.Text);
                return ValueTask.CompletedTask;
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.WasCancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (result.ExitCode != 0)
        {
            var error = errors.ToString().Trim();
            throw new YtDlpException(error.Length > 0
                ? $"yt-dlp could not list supported sites: {error}"
                : $"yt-dlp could not list supported sites (exit code {result.ExitCode}).");
        }

        return standardOutput.ToString();
    }

    private static string CreateToolFingerprint(string path)
    {
        var file = new FileInfo(path);
        return $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
    }
}
