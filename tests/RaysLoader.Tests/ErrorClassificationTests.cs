using RaysLoader.Services;

namespace RaysLoader.Tests;

public sealed class ErrorClassificationTests
{
    [Theory]
    [InlineData("ERROR: Unable to download webpage: HTTP Error 404: Not Found")]
    [InlineData("ERROR: Unable to extract image URL; please report this issue")]
    [InlineData("ERROR: Unable to download API page: <urlopen error timed out>")]
    public void WordsContainingAgeAreNotReportedAsAgeRestricted(string error)
    {
        var message = YtDlpService.ClassifyError(error);

        Assert.DoesNotContain("age restricted", message);
    }

    [Fact]
    public void WebpageDownloadFailureIsReportedAsNetworkProblem()
    {
        var message = YtDlpService.ClassifyError("ERROR: Unable to download webpage: HTTP Error 404: Not Found");

        Assert.Contains("could not be reached", message);
    }

    [Theory]
    [InlineData("ERROR: [youtube] abc: This video is age-restricted and only available on YouTube")]
    [InlineData("ERROR: This video may be inappropriate for some users.")]
    [InlineData("ERROR: [site] 123: Age restricted content")]
    public void AgeRestrictionPhrasesAreStillDetected(string error)
    {
        var message = YtDlpService.ClassifyError(error);

        Assert.Contains("age restricted", message);
    }

    [Theory]
    [InlineData("ERROR: The uploader has not made this video available in your country")]
    [InlineData("ERROR: [site] 123: This video is geo-restricted")]
    public void RegionRestrictionsAreDetected(string error)
    {
        Assert.Contains("current region", YtDlpService.ClassifyError(error));
    }

    [Fact]
    public void GeometryIsNotARegionRestriction()
    {
        Assert.DoesNotContain("current region", YtDlpService.ClassifyError("ERROR: invalid video geometry"));
    }
}
