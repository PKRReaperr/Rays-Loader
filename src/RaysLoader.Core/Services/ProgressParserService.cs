using System.Globalization;
using System.Text.RegularExpressions;
using RaysLoader.Models;

namespace RaysLoader.Services;

public interface IProgressParserService
{
    bool TryParse(string line, out ProgressSnapshot snapshot);
}

public sealed partial class ProgressParserService : IProgressParserService
{
    public bool TryParse(string line, out ProgressSnapshot snapshot)
    {
        snapshot = new ProgressSnapshot();
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var clean = AnsiRegex().Replace(line, string.Empty).Trim();
        if (clean.StartsWith(YtDlpArgumentBuilder.ProgressPrefix, StringComparison.Ordinal))
        {
            var values = clean[YtDlpArgumentBuilder.ProgressPrefix.Length..].Split('|');
            Array.Resize(ref values, 9);
            snapshot = new ProgressSnapshot(
                ParsePercent(values[0]),
                NormalizeUnknown(values[1]),
                NormalizeUnknown(values[2]),
                ParseLong(values[3]),
                ParseLong(values[4]),
                ParseInt(values[5]),
                ParseInt(values[6]),
                ParseInt(values[7]),
                ParseInt(values[8]),
                "Downloading");
            return true;
        }

        if (clean.StartsWith("[download]", StringComparison.OrdinalIgnoreCase))
        {
            var match = OrdinaryDownloadRegex().Match(clean);
            snapshot = new ProgressSnapshot(
                match.Success ? ParsePercent(match.Groups[1].Value) : null,
                match.Success ? NormalizeUnknown(match.Groups[2].Value) : string.Empty,
                match.Success ? NormalizeUnknown(match.Groups[3].Value) : string.Empty,
                Operation: "Downloading");
            return true;
        }

        var operation = clean switch
        {
            var value when value.StartsWith("[Merger]", StringComparison.OrdinalIgnoreCase) => "Merging video and audio",
            var value when value.StartsWith("[ExtractAudio]", StringComparison.OrdinalIgnoreCase) => "Extracting audio",
            var value when value.StartsWith("[VideoConvertor]", StringComparison.OrdinalIgnoreCase) => "Converting video",
            var value when value.StartsWith("[Metadata]", StringComparison.OrdinalIgnoreCase) => "Embedding metadata",
            var value when value.StartsWith("[EmbedThumbnail]", StringComparison.OrdinalIgnoreCase) => "Embedding thumbnail",
            var value when value.StartsWith("[Fixup", StringComparison.OrdinalIgnoreCase) => "Finalizing media",
            _ => string.Empty,
        };
        if (operation.Length == 0)
        {
            return false;
        }

        snapshot = new ProgressSnapshot(Operation: operation);
        return true;
    }

    private static double? ParsePercent(string? value)
    {
        var normalized = NormalizeUnknown(value).Trim().TrimEnd('%');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? Math.Clamp(result, 0, 100)
            : null;
    }

    private static long? ParseLong(string? value) => long.TryParse(NormalizeUnknown(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    private static int? ParseInt(string? value) => int.TryParse(NormalizeUnknown(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static string NormalizeUnknown(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed is "NA" or "N/A" or "None" or "null" ? string.Empty : trimmed;
    }

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")]
    private static partial Regex AnsiRegex();

    [GeneratedRegex(@"\[download\]\s+([\d.]+%)\s+.*?at\s+(\S+).*?ETA\s+(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex OrdinaryDownloadRegex();
}
