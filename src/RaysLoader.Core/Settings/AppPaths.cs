namespace RaysLoader.Settings;

public static class AppPaths
{
    public const string UserDownloadsDirectory = "%USERPROFILE%\\Downloads";

    public static string NormalizeOutputDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return UserDownloadsDirectory;

        var trimmed = path.Trim();
        try
        {
            return Path.GetFullPath(ResolveOutputDirectory(trimmed)).Equals(
                Path.GetFullPath(ResolveOutputDirectory(UserDownloadsDirectory)),
                StringComparison.OrdinalIgnoreCase)
                ? UserDownloadsDirectory
                : trimmed;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return trimmed;
        }
    }

    public static string ResolveOutputDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var expanded = path.Equals(UserDownloadsDirectory, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : Environment.ExpandEnvironmentVariables(path);
        return Path.GetFullPath(expanded);
    }
}
