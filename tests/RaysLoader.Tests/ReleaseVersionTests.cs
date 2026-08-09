using RaysLoader.Services;

namespace RaysLoader.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("0.0.0", 0, 0, 0)]
    public void ParsesStableThreeComponentTags(string tag, int major, int minor, int patch)
    {
        var version = ReleaseVersion.ParseTag(tag);

        Assert.Equal(new ReleaseVersion(major, minor, patch), version);
        Assert.Equal($"RaysLoader-Setup-{major}.{minor}.{patch}.exe", version.InstallerFileName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3-beta")]
    [InlineData("V1.2.3")]
    [InlineData("release-1.2.3")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    public void RejectsNonStableOrAmbiguousTags(string tag) =>
        Assert.Throws<InvalidDataException>(() => ReleaseVersion.ParseTag(tag));

    [Fact]
    public void AssemblyVersionUsesSameThreeComponentRepresentation() =>
        Assert.Equal(new ReleaseVersion(1, 2, 3), ReleaseVersion.FromAssemblyVersion(new Version(1, 2, 3, 0)));

    [Fact]
    public void RejectsFourComponentAssemblyVersion() =>
        Assert.Throws<InvalidDataException>(() => ReleaseVersion.FromAssemblyVersion(new Version(1, 2, 3, 4)));
}
