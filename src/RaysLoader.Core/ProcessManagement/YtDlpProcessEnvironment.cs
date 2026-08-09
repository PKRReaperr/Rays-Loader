using RaysLoader.Services;
using RaysLoader.Settings;

namespace RaysLoader.ProcessManagement;

public static class YtDlpProcessEnvironment
{
    public static IReadOnlyDictionary<string, string?> Create(ToolPaths tools) => new Dictionary<string, string?>
    {
        ["PATH"] = BuildSanitizedPath(tools, Environment.GetEnvironmentVariable("PATH")),
    };

    public static string BuildSanitizedPath(ToolPaths tools, string? inheritedPath)
    {
        var directories = new List<string>();
        AddTrustedDirectory(directories, Path.GetDirectoryName(tools.YtDlp));
        AddTrustedDirectory(directories, tools.FfmpegDirectory);
        AddTrustedDirectory(directories, Environment.SystemDirectory);

        foreach (var directory in (inheritedPath ?? string.Empty).Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddTrustedDirectory(directories, directory);
        }

        return string.Join(Path.PathSeparator, directories.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static void AddTrustedDirectory(List<string> directories, string? directory)
    {
        if (IsTrustedDirectory(directory)) directories.Add(Path.GetFullPath(directory!));
    }

    public static bool IsTrustedDirectory(string? directory)
        => PathTrust.TryGetTrustedDirectory(directory, out _);
}
