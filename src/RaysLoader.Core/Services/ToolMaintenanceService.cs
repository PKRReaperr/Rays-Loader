using System.Collections.Concurrent;
using RaysLoader.ProcessManagement;
using RaysLoader.Settings;

namespace RaysLoader.Services;

public sealed record ToolHealthSnapshot(
    string YtDlpVersion,
    string YtDlpPath,
    string YtDlpSource,
    string FfmpegVersion,
    string FfprobeVersion,
    string JavaScriptRuntime,
    bool HasYtDlp,
    bool HasFfmpeg,
    bool HasFfprobe,
    bool HasJavaScriptRuntime);

public sealed record YtDlpMaintenanceResult(ToolHealthSnapshot Snapshot, bool WasUpdated, string Message);

public interface IToolMaintenanceService
{
    ToolHealthSnapshot? LastSnapshot { get; }
    string LastMessage { get; }
    Task<ToolHealthSnapshot> CheckStatusAsync(CancellationToken cancellationToken = default);
    Task<YtDlpMaintenanceResult> CheckAndUpdateYtDlpAsync(
        YtDlpUpdateChannel channel,
        CancellationToken cancellationToken = default);
    Task WaitForIdleAsync(CancellationToken cancellationToken = default);
}

public sealed class ToolMaintenanceService : IToolMaintenanceService
{
    private const string YtDlpFileName = "yt-dlp.exe";
    private readonly IProcessRunnerService processRunner;
    private readonly IToolLocatorService toolLocator;
    private readonly ISettingsService settingsService;
    private readonly string managedToolsDirectory;
    private readonly string bundledToolsDirectory;
    private readonly SemaphoreSlim operationGate = new(1, 1);

    public ToolMaintenanceService(
        IProcessRunnerService processRunner,
        IToolLocatorService toolLocator,
        ISettingsService settingsService)
        : this(
            processRunner,
            toolLocator,
            settingsService,
            Path.Combine(settingsService.SettingsDirectory, "tools"),
            Path.Combine(AppContext.BaseDirectory, "tools"))
    {
    }

    internal ToolMaintenanceService(
        IProcessRunnerService processRunner,
        IToolLocatorService toolLocator,
        ISettingsService settingsService,
        string managedToolsDirectory,
        string? bundledToolsDirectory = null)
    {
        this.processRunner = processRunner;
        this.toolLocator = toolLocator;
        this.settingsService = settingsService;
        this.managedToolsDirectory = Path.GetFullPath(managedToolsDirectory);
        this.bundledToolsDirectory = Path.GetFullPath(bundledToolsDirectory ?? Path.Combine(AppContext.BaseDirectory, "tools"));
    }

    public ToolHealthSnapshot? LastSnapshot { get; private set; }
    public string LastMessage { get; private set; } = "Backend status has not been checked yet.";

    public async Task<ToolHealthSnapshot> CheckStatusAsync(CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LastSnapshot = await CheckStatusCoreAsync(cancellationToken).ConfigureAwait(false);
            LastMessage = CreateHealthMessage(LastSnapshot);
            return LastSnapshot;
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<YtDlpMaintenanceResult> CheckAndUpdateYtDlpAsync(
        YtDlpUpdateChannel channel,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var before = await CheckStatusCoreAsync(cancellationToken).ConfigureAwait(false);
            LastSnapshot = before;
            if (!before.HasYtDlp)
            {
                throw new MissingToolException("yt-dlp was not found. Reinstall Ray's Loader or choose a trusted yt-dlp.exe in Settings.");
            }

            var tools = toolLocator.Locate(settingsService.Current);
            if (UsesConfiguredYtDlp(tools.YtDlp))
            {
                LastMessage = "The selected custom yt-dlp is externally managed. Ray's Loader checked its version but did not replace it.";
                return new YtDlpMaintenanceResult(before, false, LastMessage);
            }

            var managedYtDlp = PrepareManagedCopy(tools.YtDlp);
            var rollbackPath = GetManagedChildPath("yt-dlp.rollback.exe");
            File.Copy(managedYtDlp, rollbackPath, overwrite: true);
            try
            {
                var update = await RunToolAsync(
                    managedYtDlp,
                    ["--ignore-config", "--update-to", $"{channel.ToString().ToLowerInvariant()}@latest"],
                    tools,
                    cancellationToken).ConfigureAwait(false);
                if (update.Result.WasCancelled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                if (update.Result.ExitCode != 0)
                {
                    var detail = FirstNonEmptyLine(update.StandardError, update.StandardOutput);
                    throw new YtDlpException(detail.Length > 0
                        ? $"yt-dlp could not update: {detail}"
                        : $"yt-dlp could not update (exit code {update.Result.ExitCode}).");
                }

                if (!PathTrust.TryGetTrustedExecutable(managedYtDlp, out _))
                {
                    throw new InvalidDataException("The updated yt-dlp executable failed path validation.");
                }
            }
            catch
            {
                RestoreRollback(rollbackPath, managedYtDlp);
                throw;
            }

            DeleteManagedFile(rollbackPath);
            LastSnapshot = await CheckStatusCoreAsync(cancellationToken).ConfigureAwait(false);
            var wasUpdated = !LastSnapshot.YtDlpVersion.Equals(before.YtDlpVersion, StringComparison.OrdinalIgnoreCase);
            LastMessage = wasUpdated
                ? $"yt-dlp updated from {before.YtDlpVersion} to {LastSnapshot.YtDlpVersion}."
                : $"yt-dlp {LastSnapshot.YtDlpVersion} is current on the {channel.ToString().ToLowerInvariant()} channel.";
            return new YtDlpMaintenanceResult(LastSnapshot, wasUpdated, LastMessage);
        }
        catch (OperationCanceledException)
        {
            LastMessage = "yt-dlp update cancelled. The previous backend was restored.";
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or YtDlpException or MissingToolException or ProcessLaunchException)
        {
            LastMessage = $"yt-dlp update failed: {exception.Message}";
            throw;
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        operationGate.Release();
    }

    private async Task<ToolHealthSnapshot> CheckStatusCoreAsync(CancellationToken cancellationToken)
    {
        var tools = toolLocator.Locate(settingsService.Current);
        var hasYtDlp = File.Exists(tools.YtDlp);
        var hasFfmpeg = File.Exists(tools.Ffmpeg);
        var hasFfprobe = File.Exists(tools.Ffprobe);
        var ytDlpVersion = hasYtDlp
            ? await ReadVersionAsync(tools.YtDlp, ["--ignore-config", "--version"], tools, cancellationToken).ConfigureAwait(false)
            : "Not found";
        var ffmpegVersion = hasFfmpeg
            ? await ReadVersionAsync(tools.Ffmpeg, ["-version"], tools, cancellationToken).ConfigureAwait(false)
            : "Not found";
        var ffprobeVersion = hasFfprobe
            ? await ReadVersionAsync(tools.Ffprobe, ["-version"], tools, cancellationToken).ConfigureAwait(false)
            : "Not found";
        var javaScriptRuntime = await ReadJavaScriptRuntimeAsync(tools, cancellationToken).ConfigureAwait(false);

        return new ToolHealthSnapshot(
            NormalizeVersion(ytDlpVersion),
            tools.YtDlp,
            GetToolSource(tools.YtDlp),
            NormalizeVersion(ffmpegVersion),
            NormalizeVersion(ffprobeVersion),
            javaScriptRuntime,
            hasYtDlp,
            hasFfmpeg,
            hasFfprobe,
            tools.JavaScriptRuntime.Length > 0);
    }

    private async Task<string> ReadJavaScriptRuntimeAsync(ToolPaths tools, CancellationToken cancellationToken)
    {
        var separator = tools.JavaScriptRuntime.IndexOf(':');
        if (separator <= 0 || separator == tools.JavaScriptRuntime.Length - 1)
        {
            return "Not found";
        }

        var name = tools.JavaScriptRuntime[..separator];
        var path = tools.JavaScriptRuntime[(separator + 1)..];
        if (!File.Exists(path)) return "Not found";
        var version = await ReadVersionAsync(path, ["--version"], tools, cancellationToken).ConfigureAwait(false);
        return $"{name} {NormalizeVersion(version)}";
    }

    private async Task<string> ReadVersionAsync(
        string executable,
        IReadOnlyList<string> arguments,
        ToolPaths tools,
        CancellationToken cancellationToken)
    {
        var output = await RunToolAsync(executable, arguments, tools, cancellationToken).ConfigureAwait(false);
        if (output.Result.WasCancelled) throw new OperationCanceledException(cancellationToken);
        if (output.Result.ExitCode != 0) return "Unavailable";
        return FirstNonEmptyLine(output.StandardOutput, output.StandardError) is { Length: > 0 } line
            ? line
            : "Unknown";
    }

    private async Task<ToolOutput> RunToolAsync(
        string executable,
        IReadOnlyList<string> arguments,
        ToolPaths tools,
        CancellationToken cancellationToken)
    {
        var standardOutput = new ConcurrentQueue<string>();
        var standardError = new ConcurrentQueue<string>();
        var result = await processRunner.RunAsync(
            new ProcessRequest(executable, arguments, Environment: YtDlpProcessEnvironment.Create(tools)),
            line =>
            {
                (line.IsError ? standardError : standardOutput).Enqueue(line.Text);
                return ValueTask.CompletedTask;
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return new ToolOutput(result, standardOutput.ToArray(), standardError.ToArray());
    }

    private string PrepareManagedCopy(string sourcePath)
    {
        Directory.CreateDirectory(managedToolsDirectory);
        if (!PathTrust.TryGetTrustedDirectory(managedToolsDirectory, out _))
        {
            throw new InvalidDataException("The managed tools directory failed path validation.");
        }

        var targetPath = GetManagedChildPath(YtDlpFileName);
        if (Path.GetFullPath(sourcePath).Equals(targetPath, StringComparison.OrdinalIgnoreCase)) return targetPath;

        var stagingPath = GetManagedChildPath("yt-dlp.seed.download");
        DeleteManagedFile(stagingPath);
        File.Copy(sourcePath, stagingPath, overwrite: false);
        File.Move(stagingPath, targetPath, overwrite: true);
        if (!PathTrust.TryGetTrustedExecutable(targetPath, out var trustedTarget))
        {
            throw new InvalidDataException("The managed yt-dlp copy failed path validation.");
        }
        return trustedTarget;
    }

    private string GetManagedChildPath(string fileName)
    {
        if (!PathTrust.TryGetSafeChildPath(managedToolsDirectory, fileName, out var path))
        {
            throw new InvalidDataException($"The managed tool filename '{fileName}' is invalid.");
        }
        return path;
    }

    private bool UsesConfiguredYtDlp(string locatedPath) =>
        PathTrust.TryGetTrustedExecutable(settingsService.Current.YtDlpPath, out var configuredPath) &&
        configuredPath.Equals(locatedPath, StringComparison.OrdinalIgnoreCase);

    private string GetToolSource(string path)
    {
        if (path.Length == 0) return "Missing";
        var fullPath = Path.GetFullPath(path);
        if (Path.GetDirectoryName(fullPath)!.Equals(managedToolsDirectory, StringComparison.OrdinalIgnoreCase)) return "Managed by Ray's Loader";
        if (Path.GetDirectoryName(fullPath)!.Equals(bundledToolsDirectory, StringComparison.OrdinalIgnoreCase)) return "Bundled with Ray's Loader";
        if (UsesConfiguredYtDlp(fullPath)) return "Custom selection";
        return "System or legacy tool";
    }

    private static void RestoreRollback(string rollbackPath, string targetPath)
    {
        if (!File.Exists(rollbackPath)) return;
        File.Copy(rollbackPath, targetPath, overwrite: true);
        File.Delete(rollbackPath);
    }

    private static void DeleteManagedFile(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static string CreateHealthMessage(ToolHealthSnapshot snapshot)
    {
        if (!snapshot.HasYtDlp) return "yt-dlp is missing.";
        if (!snapshot.HasFfmpeg || !snapshot.HasFfprobe) return "yt-dlp is available, but FFmpeg or ffprobe is missing.";
        if (!snapshot.HasJavaScriptRuntime) return "Core tools are available. Install Deno 2.3+ or Node 22+ for reliable YouTube extraction.";
        return "The download engine is ready.";
    }

    private static string NormalizeVersion(string value)
    {
        var trimmed = value.Trim();
        foreach (var prefix in new[] { "ffmpeg version ", "ffprobe version ", "v" })
        {
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            trimmed = trimmed[prefix.Length..];
            break;
        }
        var separator = trimmed.IndexOf(' ');
        return separator > 0 ? trimmed[..separator] : trimmed;
    }

    private static string FirstNonEmptyLine(params IReadOnlyList<string>[] sources) =>
        sources.SelectMany(source => source).FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim() ?? string.Empty;

    private sealed record ToolOutput(ProcessRunResult Result, IReadOnlyList<string> StandardOutput, IReadOnlyList<string> StandardError);
}
