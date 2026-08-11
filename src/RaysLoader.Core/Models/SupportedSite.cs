namespace RaysLoader.Models;

public sealed record SupportedSite(
    string ExtractorName,
    string SiteName,
    string Detail,
    bool IsKnownBroken,
    bool IsGeneric);

public sealed record SupportedSitesCatalog(
    string YtDlpVersion,
    IReadOnlyList<SupportedSite> Sites);

public static class SupportedSiteLinks
{
    public static Uri OfficialSupportedSites { get; } =
        new("https://github.com/yt-dlp/yt-dlp/blob/master/supportedsites.md");

    public static Uri CreateIssueSearch(SupportedSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        var searchTerm = string.IsNullOrWhiteSpace(site.SiteName) ? site.ExtractorName : site.SiteName;
        var query = $"is:issue sort:created-asc \"{searchTerm}\"";
        return new Uri($"https://github.com/yt-dlp/yt-dlp/issues?q={Uri.EscapeDataString(query)}");
    }
}
