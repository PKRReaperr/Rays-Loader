using RaysLoader.Settings;

namespace RaysLoader.Services;

public sealed record ToolPaths(string YtDlp, string Ffmpeg, string Ffprobe, string JavaScriptRuntime = "")
{
    public string? FfmpegDirectory => Path.GetDirectoryName(Ffmpeg);
}

public interface IToolLocatorService
{
    ToolPaths Locate(AppSettings settings);
}

public sealed class ToolLocatorService : IToolLocatorService
{
    public ToolPaths Locate(AppSettings settings)
    {
        var applicationTools = Path.Combine(AppContext.BaseDirectory, "tools");
        var localRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var managedTools = Path.Combine(localRoot, "RaysLoader", "tools");
        var legacyTools = Path.Combine(localRoot, "YtDlpGui", "tools");

        return new ToolPaths(
            FindTool(settings.YtDlpPath, "yt-dlp.exe", applicationTools, managedTools, legacyTools),
            FindTool(settings.FfmpegPath, "ffmpeg.exe", applicationTools, managedTools, legacyTools),
            FindTool(string.Empty, "ffprobe.exe", applicationTools, managedTools, legacyTools),
            FindJavaScriptRuntime(applicationTools, managedTools, legacyTools));
    }

    private static string FindTool(string configured, string name, params string[] directories)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        foreach (var directory in directories)
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directories[1], name);
    }

    private static string FindJavaScriptRuntime(params string[] directories)
    {
        var deno = FindOptionalTool("deno.exe", directories);
        if (deno.Length > 0) return $"deno:{deno}";

        var node = FindOptionalTool("node.exe", directories);
        return node.Length > 0 ? $"node:{node}" : string.Empty;
    }

    private static string FindOptionalTool(string name, params string[] directories)
    {
        foreach (var directory in directories)
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        return string.Empty;
    }
}
