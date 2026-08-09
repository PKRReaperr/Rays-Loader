using RaysLoader.Services;
using RaysLoader.Settings;
using System.Diagnostics;

namespace RaysLoader.Tests;

public sealed class ToolLocatorServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "RaysLoader.Tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> junctions = [];

    [Fact]
    public void AcceptsExplicitTrustedExecutable()
    {
        var configured = CreateExecutable(Path.Combine(root, "configured"), "renamed.exe");
        var service = CreateLocator(pathValue: string.Empty);

        var tools = service.Locate(new AppSettings { YtDlpPath = configured });

        Assert.Equal(configured, tools.YtDlp, ignoreCase: true);
    }

    [Fact]
    public void MissingConfiguredExecutableFallsBackToTrustedManagedCandidate()
    {
        var managed = Path.Combine(root, "managed");
        var expected = CreateExecutable(managed, "yt-dlp.exe");
        var service = CreateLocator(string.Empty, Path.Combine(root, "bundled"), managed, Path.Combine(root, "legacy"));

        var tools = service.Locate(new AppSettings { YtDlpPath = Path.Combine(root, "missing.exe") });

        Assert.Equal(expected, tools.YtDlp, ignoreCase: true);
    }

    [Fact]
    public void FindsExecutableInTrustedPathEntry()
    {
        var pathDirectory = Path.Combine(root, "path tools");
        var expected = CreateExecutable(pathDirectory, "yt-dlp.exe");
        var service = CreateLocator(pathDirectory);

        var tools = service.Locate(new AppSettings());

        Assert.Equal(expected, tools.YtDlp, ignoreCase: true);
    }

    [Fact]
    public void RejectsConfiguredExecutableInsideReparsePointDirectory()
    {
        var target = Path.Combine(root, "target");
        var executable = CreateExecutable(target, "yt-dlp.exe");
        var link = Path.Combine(root, "linked");
        CreateJunction(link, target);
        var trustedFallback = CreateExecutable(Path.Combine(root, "managed"), "yt-dlp.exe");
        var service = CreateLocator(string.Empty, Path.Combine(root, "bundled"), Path.GetDirectoryName(trustedFallback)!, Path.Combine(root, "legacy"));

        var tools = service.Locate(new AppSettings { YtDlpPath = Path.Combine(link, Path.GetFileName(executable)) });

        Assert.Equal(trustedFallback, tools.YtDlp, ignoreCase: true);
        Assert.Single(tools.TrustWarnings);
        Assert.Contains("reparse point", tools.TrustWarnings[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsReparsePointPathEntry()
    {
        var target = Path.Combine(root, "path-target");
        _ = CreateExecutable(target, "yt-dlp.exe");
        var link = Path.Combine(root, "path-link");
        CreateJunction(link, target);
        var managed = Path.Combine(root, "managed");
        var service = CreateLocator(link, Path.Combine(root, "bundled"), managed, Path.Combine(root, "legacy"));

        var tools = service.Locate(new AppSettings());

        Assert.Equal(Path.Combine(managed, "yt-dlp.exe"), tools.YtDlp, ignoreCase: true);
    }

    public void Dispose()
    {
        foreach (var junction in junctions.Where(Directory.Exists)) Directory.Delete(junction);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private ToolLocatorService CreateLocator(string pathValue, params string[] directories)
    {
        if (directories.Length == 0)
            directories = [Path.Combine(root, "bundled"), Path.Combine(root, "managed"), Path.Combine(root, "legacy")];
        return new ToolLocatorService(directories, () => pathValue);
    }

    private static string CreateExecutable(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, [0x4D, 0x5A]);
        return Path.GetFullPath(path);
    }

    private void CreateJunction(string link, string target)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(link);
        startInfo.ArgumentList.Add(target);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start mklink.");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Could not create test junction: {process.StandardError.ReadToEnd()}");
        junctions.Add(link);
    }
}
