using RaysLoader.Settings;

namespace RaysLoader.Tests;

public sealed class AppPathsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("%USERPROFILE%\\Downloads")]
    public void EmptyAndPortableDefaultsResolveToCurrentUsersDownloads(string? value) =>
        Assert.Equal(AppPaths.DefaultOutputDirectory, AppPaths.NormalizeOutputDirectory(value));

    [Fact]
    public void ResolvesSpacesAndUnicodeInAbsolutePath()
    {
        var input = Path.Combine(Path.GetPathRoot(AppPaths.DefaultOutputDirectory)!, "Media Files", "Vidéos_日本語");

        Assert.Equal(Path.GetFullPath(input), AppPaths.ResolveOutputDirectory(input));
    }

    [Fact]
    public void ResolvesKnownEnvironmentVariable()
    {
        const string variable = "RAYS_LOADER_PATH_TEST";
        var previous = Environment.GetEnvironmentVariable(variable);
        var root = Path.Combine(Path.GetPathRoot(AppPaths.DefaultOutputDirectory)!, "Ray's Loader Test");
        try
        {
            Environment.SetEnvironmentVariable(variable, root);
            Assert.Equal(Path.Combine(root, "Videos"), AppPaths.ResolveOutputDirectory($"%{variable}%\\Videos"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public void NormalizesLongAbsolutePath()
    {
        var input = Path.Combine(Path.GetPathRoot(AppPaths.DefaultOutputDirectory)!, string.Join("\\", Enumerable.Repeat("long-folder-name", 20)));

        Assert.Equal(Path.GetFullPath(input), AppPaths.ResolveOutputDirectory(input));
    }

    [Theory]
    [InlineData("relative\\Downloads")]
    [InlineData("%RAYS_LOADER_UNKNOWN%\\Downloads")]
    [InlineData("\\\\?\\C:\\Downloads")]
    [InlineData("\\\\.\\C:\\Downloads")]
    public void RejectsUnexpectedPaths(string value)
    {
        var exception = Assert.Throws<FormatException>(() => AppPaths.ResolveOutputDirectory(value));

        Assert.Contains("output folder", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsNullCharacters() =>
        Assert.Throws<FormatException>(() => AppPaths.ResolveOutputDirectory("C:\\Media\0Unsafe"));
}
