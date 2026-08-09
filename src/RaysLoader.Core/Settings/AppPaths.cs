namespace RaysLoader.Settings;

public static class AppPaths
{
    public const string UserDownloadsDirectory = "%USERPROFILE%\\Downloads";
    public static string DefaultOutputDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads");

    public static string NormalizeOutputDirectory(string? path)
        => ResolveOutputDirectory(string.IsNullOrWhiteSpace(path) ? UserDownloadsDirectory : path);

    public static string ResolveOutputDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) path = UserDownloadsDirectory;
        var trimmed = path.Trim();
        try
        {
            if (trimmed.IndexOf('\0') >= 0) throw new ArgumentException("The path contains a null character.");
            var expanded = trimmed.Equals(UserDownloadsDirectory, StringComparison.OrdinalIgnoreCase)
                ? DefaultOutputDirectory
                : Environment.ExpandEnvironmentVariables(trimmed);
            if (ContainsUnexpandedEnvironmentVariable(expanded))
                throw new ArgumentException("The path contains an unknown environment variable.");
            if (expanded.StartsWith("\\\\?\\", StringComparison.Ordinal) || expanded.StartsWith("\\\\.\\", StringComparison.Ordinal))
                throw new ArgumentException("Device paths are not supported.");
            if (!Path.IsPathFullyQualified(expanded))
                throw new ArgumentException("The path must resolve to an absolute folder.");

            var fullPath = Path.GetFullPath(expanded);
            if (!Path.IsPathFullyQualified(fullPath)) throw new ArgumentException("The path did not resolve to an absolute folder.");
            return fullPath;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new FormatException($"The output folder '{trimmed}' is invalid: {exception.Message}", exception);
        }
    }

    private static bool ContainsUnexpandedEnvironmentVariable(string path)
    {
        var opening = path.IndexOf('%');
        while (opening >= 0)
        {
            var closing = path.IndexOf('%', opening + 1);
            if (closing > opening + 1) return true;
            if (closing < 0) return false;
            opening = path.IndexOf('%', closing + 1);
        }
        return false;
    }
}
