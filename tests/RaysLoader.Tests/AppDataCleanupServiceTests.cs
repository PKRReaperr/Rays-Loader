using System.Diagnostics;
using RaysLoader.Services;

namespace RaysLoader.Tests;

public sealed class AppDataCleanupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"RaysLoaderCleanup-{Guid.NewGuid():N}");

    [Fact]
    public void CleanupRemovesOnlySelectedOwnedData()
    {
        var settings = Path.Combine(_root, "RaysLoader");
        var legacyLocal = Path.Combine(_root, "Local", "YtDlpGui");
        var legacyRoaming = Path.Combine(_root, "Roaming", "YtDlpGui");
        var download = Path.Combine(_root, "Downloads", "keep.mp4");
        WriteFile(Path.Combine(settings, "tools", "yt-dlp.exe"));
        WriteFile(Path.Combine(settings, "updates", "RaysLoader-Setup-1.2.0.exe"));
        WriteFile(Path.Combine(settings, "crash.log"));
        WriteFile(Path.Combine(settings, "settings.corrupt-20260101-120000.json"));
        WriteFile(Path.Combine(settings, "settings.json"));
        WriteFile(Path.Combine(settings, "unrelated.keep"));
        WriteFile(Path.Combine(legacyLocal, "tools", "ffmpeg.exe"));
        WriteFile(Path.Combine(legacyRoaming, "settings.json"));
        WriteFile(download);

        var service = new AppDataCleanupService(settings, legacyLocal, legacyRoaming);
        var report = service.Cleanup(
            AppDataCleanupTargets.ManagedTools |
            AppDataCleanupTargets.UpdateArtifacts |
            AppDataCleanupTargets.Diagnostics |
            AppDataCleanupTargets.LegacyData);

        Assert.True(report.Succeeded);
        Assert.False(Directory.Exists(Path.Combine(settings, "tools")));
        Assert.False(Directory.Exists(Path.Combine(settings, "updates")));
        Assert.False(File.Exists(Path.Combine(settings, "crash.log")));
        Assert.False(File.Exists(Path.Combine(settings, "settings.corrupt-20260101-120000.json")));
        Assert.False(Directory.Exists(legacyLocal));
        Assert.False(Directory.Exists(legacyRoaming));
        Assert.True(File.Exists(Path.Combine(settings, "settings.json")));
        Assert.True(File.Exists(Path.Combine(settings, "unrelated.keep")));
        Assert.True(File.Exists(download));
    }

    [Fact]
    public void UnselectedDataIsPreserved()
    {
        var settings = Path.Combine(_root, "RaysLoader");
        var legacyLocal = Path.Combine(_root, "Local", "YtDlpGui");
        var legacyRoaming = Path.Combine(_root, "Roaming", "YtDlpGui");
        WriteFile(Path.Combine(settings, "tools", "yt-dlp.exe"));
        WriteFile(Path.Combine(settings, "updates", "installer.exe"));

        var report = new AppDataCleanupService(settings, legacyLocal, legacyRoaming)
            .Cleanup(AppDataCleanupTargets.ManagedTools);

        Assert.True(report.Succeeded);
        Assert.False(Directory.Exists(Path.Combine(settings, "tools")));
        Assert.True(Directory.Exists(Path.Combine(settings, "updates")));
    }

    [Fact]
    public void MissingOwnedDirectoriesAreSuccessfulNoOps()
    {
        var service = new AppDataCleanupService(
            Path.Combine(_root, "RaysLoader"),
            Path.Combine(_root, "Local", "YtDlpGui"),
            Path.Combine(_root, "Roaming", "YtDlpGui"));

        var report = service.Cleanup((AppDataCleanupTargets)(-1));

        Assert.True(report.Succeeded);
        Assert.Empty(report.RemovedPaths);
    }

    [Fact]
    public void CleanupRefusesDirectoryContainingReparsePoint()
    {
        if (!OperatingSystem.IsWindows()) return;
        var settings = Path.Combine(_root, "RaysLoader");
        var updates = Path.Combine(settings, "updates");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(updates);
        WriteFile(Path.Combine(outside, "keep.txt"));
        CreateJunction(Path.Combine(updates, "linked"), outside);

        var report = new AppDataCleanupService(
            settings,
            Path.Combine(_root, "Local", "YtDlpGui"),
            Path.Combine(_root, "Roaming", "YtDlpGui"))
            .Cleanup(AppDataCleanupTargets.UpdateArtifacts);

        Assert.False(report.Succeeded);
        Assert.Single(report.Failures);
        Assert.True(Directory.Exists(updates));
        Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
    }

    [Fact]
    public void LockedFileFailureIsReportedAndDataIsPreserved()
    {
        if (!OperatingSystem.IsWindows()) return;
        var settings = Path.Combine(_root, "RaysLoader");
        var installer = Path.Combine(settings, "updates", "RaysLoader-Setup-1.2.0.exe");
        WriteFile(installer);
        using var locked = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.None);

        var report = new AppDataCleanupService(
            settings,
            Path.Combine(_root, "Local", "YtDlpGui"),
            Path.Combine(_root, "Roaming", "YtDlpGui"))
            .Cleanup(AppDataCleanupTargets.UpdateArtifacts);

        Assert.False(report.Succeeded);
        Assert.Single(report.Failures);
        Assert.True(File.Exists(installer));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void WriteFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "test");
    }

    private static void CreateJunction(string link, string target)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(link);
        startInfo.ArgumentList.Add(target);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not create test junction.");
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
