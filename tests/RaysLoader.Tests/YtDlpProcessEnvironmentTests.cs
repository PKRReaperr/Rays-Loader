using RaysLoader.ProcessManagement;
using RaysLoader.Services;

namespace RaysLoader.Tests;

public sealed class YtDlpProcessEnvironmentTests : IDisposable
{
    private readonly string _trustedDirectory = Path.Combine(Path.GetTempPath(), "RaysLoader.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SanitizedPathKeepsExistingDirectoriesAndDropsMissingOnes()
    {
        Directory.CreateDirectory(_trustedDirectory);
        var missing = Path.Combine(_trustedDirectory, "missing");
        var tools = new ToolPaths(
            Path.Combine(_trustedDirectory, "yt-dlp.exe"),
            Path.Combine(_trustedDirectory, "ffmpeg.exe"),
            Path.Combine(_trustedDirectory, "ffprobe.exe"));

        var result = YtDlpProcessEnvironment.BuildSanitizedPath(
            tools,
            string.Join(Path.PathSeparator, _trustedDirectory, missing, _trustedDirectory));

        Assert.Contains(_trustedDirectory, result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(missing, result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, result.Split(Path.PathSeparator).Count(path => path.Equals(_trustedDirectory, StringComparison.OrdinalIgnoreCase)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_trustedDirectory)) Directory.Delete(_trustedDirectory, recursive: true);
    }
}
