using RaysLoader.Settings;

namespace RaysLoader.Services;

public sealed record ToolPaths(string YtDlp, string Ffmpeg, string Ffprobe, string JavaScriptRuntime = "")
{
    public string? FfmpegDirectory => Path.GetDirectoryName(Ffmpeg);
    public IReadOnlyList<string> TrustWarnings { get; init; } = [];
}

public interface IToolLocatorService
{
    ToolPaths Locate(AppSettings settings);
}

public sealed class ToolLocatorService : IToolLocatorService
{
    private readonly string[]? searchDirectories;
    private readonly Func<string?> pathProvider;

    public ToolLocatorService() : this(null, () => Environment.GetEnvironmentVariable("PATH")) { }

    internal ToolLocatorService(string[]? searchDirectories, Func<string?> pathProvider)
    {
        this.searchDirectories = searchDirectories;
        this.pathProvider = pathProvider;
    }

    public ToolPaths Locate(AppSettings settings)
    {
        var applicationTools = Path.Combine(AppContext.BaseDirectory, "tools");
        var localRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var managedTools = Path.Combine(localRoot, "RaysLoader", "tools");
        var legacyTools = Path.Combine(localRoot, "YtDlpGui", "tools");

        // Managed updates intentionally override the installer-bundled fallback.
        // Explicit user-selected paths still take precedence in FindTool.
        var directories = searchDirectories ?? [managedTools, applicationTools, legacyTools];
        var trustWarnings = new List<string>();
        return new ToolPaths(
            FindTool(settings.YtDlpPath, "yt-dlp.exe", trustWarnings, directories),
            FindTool(settings.FfmpegPath, "ffmpeg.exe", trustWarnings, directories),
            FindTool(string.Empty, "ffprobe.exe", directories),
            FindJavaScriptRuntime(directories))
        {
            TrustWarnings = trustWarnings,
        };
    }

    private string FindTool(string configured, string name, params string[] directories) =>
        FindTool(configured, name, null, directories);

    private string FindTool(string configured, string name, List<string>? trustWarnings, params string[] directories)
    {
        // User-selected executables may be renamed, but they must be absolute, existing
        // .exe files whose file and directory chain contain no reparse points.
        if (PathTrust.TryGetTrustedExecutable(configured, out var configuredPath))
        {
            return configuredPath;
        }
        if (!string.IsNullOrWhiteSpace(configured))
            trustWarnings?.Add($"The configured {name} path was ignored because it is missing, malformed, or uses a reparse point.");

        foreach (var directory in directories)
        {
            if (PathTrust.TryGetSafeChildPath(directory, name, out var candidate) &&
                PathTrust.TryGetTrustedExecutable(candidate, out var trustedCandidate))
            {
                return trustedCandidate;
            }
        }

        var pathValue = pathProvider() ?? string.Empty;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!PathTrust.TryGetTrustedDirectory(directory, out var trustedDirectory) ||
                !PathTrust.TryGetSafeChildPath(trustedDirectory, name, out var candidate)) continue;
            if (PathTrust.TryGetTrustedExecutable(candidate, out var trustedCandidate))
            {
                return trustedCandidate;
            }
        }

        return directories.Length > 0 && PathTrust.TryGetSafeChildPath(directories[0], name, out var fallback) ? fallback : string.Empty;
    }

    private string FindJavaScriptRuntime(params string[] directories)
    {
        var deno = FindOptionalTool("deno.exe", directories);
        if (deno.Length == 0)
            deno = FindConventionalRuntime("deno.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".deno", "bin"));
        if (deno.Length > 0) return $"deno:{deno}";

        var node = FindOptionalTool("node.exe", directories);
        if (node.Length == 0)
            node = FindConventionalRuntime("node.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"));
        return node.Length > 0 ? $"node:{node}" : string.Empty;
    }

    private static string FindConventionalRuntime(string name, string directory)
    {
        if (!PathTrust.TryGetTrustedDirectory(directory, out var trustedDirectory) ||
            !PathTrust.TryGetSafeChildPath(trustedDirectory, name, out var candidate)) return string.Empty;
        return PathTrust.TryGetTrustedExecutable(candidate, out var executable) ? executable : string.Empty;
    }

    private string FindOptionalTool(string name, params string[] directories)
    {
        foreach (var directory in directories)
        {
            if (PathTrust.TryGetSafeChildPath(directory, name, out var candidate) &&
                PathTrust.TryGetTrustedExecutable(candidate, out var trustedCandidate)) return trustedCandidate;
        }

        var pathValue = pathProvider() ?? string.Empty;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!PathTrust.TryGetTrustedDirectory(directory, out var trustedDirectory) ||
                !PathTrust.TryGetSafeChildPath(trustedDirectory, name, out var candidate)) continue;
            if (PathTrust.TryGetTrustedExecutable(candidate, out var trustedCandidate)) return trustedCandidate;
        }

        return string.Empty;
    }
}
