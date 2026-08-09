namespace RaysLoader.Settings;

internal static class PathTrust
{
    public static bool IsPlainFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName)) return false;
        return fileName.Equals(Path.GetFileName(fileName), StringComparison.Ordinal) &&
               fileName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0;
    }

    public static bool TryGetSafeChildPath(string directory, string fileName, out string fullPath)
    {
        fullPath = string.Empty;
        if (!IsPlainFileName(fileName) || !Path.IsPathFullyQualified(directory)) return false;
        try
        {
            var fullDirectory = Path.GetFullPath(directory);
            if (!HasTrustedExistingAncestors(fullDirectory)) return false;
            var candidate = Path.GetFullPath(Path.Combine(fullDirectory, fileName));
            if (!Path.GetDirectoryName(candidate)!.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase)) return false;
            fullPath = candidate;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public static bool TryGetTrustedDirectory(string? directory, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)) return false;

        try
        {
            var candidate = new DirectoryInfo(Path.GetFullPath(directory));
            if (!candidate.Exists) return false;
            for (DirectoryInfo? current = candidate; current is not null; current = current.Parent)
            {
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            }
            fullPath = candidate.FullName;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public static bool TryGetTrustedExecutable(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;

        try
        {
            var file = new FileInfo(Path.GetFullPath(path));
            if (!file.Exists || !file.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Directory is null ||
                !TryGetTrustedDirectory(file.Directory.FullName, out _))
            {
                return false;
            }
            fullPath = file.FullName;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool HasTrustedExistingAncestors(string path)
    {
        DirectoryInfo? current = new(path);
        while (current is not null && !current.Exists) current = current.Parent;
        if (current is null) return false;
        for (; current is not null; current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        }
        return true;
    }
}
