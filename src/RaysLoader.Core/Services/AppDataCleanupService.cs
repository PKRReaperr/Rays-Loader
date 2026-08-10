using RaysLoader.Settings;

namespace RaysLoader.Services;

[Flags]
public enum AppDataCleanupTargets
{
    None = 0,
    ManagedTools = 1,
    UpdateArtifacts = 2,
    Diagnostics = 4,
    LegacyData = 8,
}

public sealed record AppDataCleanupFailure(string Path, string Message);

public sealed record AppDataCleanupReport(
    IReadOnlyList<string> RemovedPaths,
    IReadOnlyList<AppDataCleanupFailure> Failures)
{
    public bool Succeeded => Failures.Count == 0;
}

public interface IAppDataCleanupService
{
    AppDataCleanupReport Cleanup(AppDataCleanupTargets targets);
}

/// <summary>
/// Removes only data created in Ray's Loader-owned locations. Download folders,
/// configured external tools, and cookie files are intentionally out of scope.
/// </summary>
public sealed class AppDataCleanupService : IAppDataCleanupService
{
    private readonly string _settingsDirectory;
    private readonly string _legacyLocalDirectory;
    private readonly string _legacyRoamingDirectory;

    public AppDataCleanupService(ISettingsService settingsService)
        : this(
            settingsService.SettingsDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YtDlpGui"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YtDlpGui"))
    {
    }

    internal AppDataCleanupService(string settingsDirectory, string legacyLocalDirectory, string legacyRoamingDirectory)
    {
        _settingsDirectory = Path.GetFullPath(settingsDirectory);
        _legacyLocalDirectory = Path.GetFullPath(legacyLocalDirectory);
        _legacyRoamingDirectory = Path.GetFullPath(legacyRoamingDirectory);
    }

    public AppDataCleanupReport Cleanup(AppDataCleanupTargets targets)
    {
        var removed = new List<string>();
        var failures = new List<AppDataCleanupFailure>();

        if (targets.HasFlag(AppDataCleanupTargets.ManagedTools))
            DeleteOwnedDirectory(_settingsDirectory, "tools", removed, failures);
        if (targets.HasFlag(AppDataCleanupTargets.UpdateArtifacts))
            DeleteOwnedDirectory(_settingsDirectory, "updates", removed, failures);
        if (targets.HasFlag(AppDataCleanupTargets.Diagnostics))
            DeleteDiagnostics(removed, failures);
        if (targets.HasFlag(AppDataCleanupTargets.LegacyData))
        {
            DeleteExactDirectory(_legacyLocalDirectory, removed, failures);
            if (!_legacyRoamingDirectory.Equals(_legacyLocalDirectory, StringComparison.OrdinalIgnoreCase))
                DeleteExactDirectory(_legacyRoamingDirectory, removed, failures);
        }

        return new AppDataCleanupReport(removed, failures);
    }

    private void DeleteOwnedDirectory(
        string parent,
        string childName,
        ICollection<string> removed,
        ICollection<AppDataCleanupFailure> failures)
    {
        var expected = Path.GetFullPath(Path.Combine(parent, childName));
        if (!Path.GetDirectoryName(expected)!.Equals(parent, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(new(expected, "The managed path did not remain inside the application data directory."));
            return;
        }

        DeleteExactDirectory(expected, removed, failures);
    }

    private void DeleteDiagnostics(ICollection<string> removed, ICollection<AppDataCleanupFailure> failures)
    {
        if (!Directory.Exists(_settingsDirectory)) return;
        if (!PathTrust.TryGetTrustedDirectory(_settingsDirectory, out var trustedDirectory))
        {
            failures.Add(new(_settingsDirectory, "The application data directory is missing or passes through a reparse point."));
            return;
        }

        DeleteExactFile(Path.Combine(trustedDirectory, "crash.log"), removed, failures);
        try
        {
            foreach (var path in Directory.EnumerateFiles(trustedDirectory, "settings.corrupt-*.json", SearchOption.TopDirectoryOnly))
                DeleteExactFile(path, removed, failures);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failures.Add(new(trustedDirectory, exception.Message));
        }
    }

    private static void DeleteExactDirectory(
        string path,
        ICollection<string> removed,
        ICollection<AppDataCleanupFailure> failures)
    {
        if (!Directory.Exists(path)) return;

        try
        {
            if (!PathTrust.TryGetTrustedDirectory(path, out var trustedDirectory))
            {
                failures.Add(new(path, "The directory passes through a reparse point and was not removed."));
                return;
            }

            var nestedReparsePoint = FindNestedReparsePoint(trustedDirectory);
            if (nestedReparsePoint is not null)
            {
                failures.Add(new(nestedReparsePoint, "A reparse point was found inside the directory; cleanup was stopped."));
                return;
            }

            Directory.Delete(trustedDirectory, recursive: true);
            removed.Add(trustedDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failures.Add(new(path, exception.Message));
        }
    }

    private static void DeleteExactFile(
        string path,
        ICollection<string> removed,
        ICollection<AppDataCleanupFailure> failures)
    {
        if (!File.Exists(path)) return;

        try
        {
            var file = new FileInfo(Path.GetFullPath(path));
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                failures.Add(new(file.FullName, "A reparse-point file was not removed."));
                return;
            }

            File.Delete(file.FullName);
            removed.Add(file.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failures.Add(new(path, exception.Message));
        }
    }

    private static string? FindNestedReparsePoint(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return entry;
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }

        return null;
    }
}
