using RaysLoader.Models;
using RaysLoader.Services;

namespace RaysLoader.Tests;

public sealed class UrlAndStateTests
{
    [Fact]
    public void ParsesMultipleUrlsAndRemovesDuplicates()
    {
        var result = UrlInputParser.Parse("https://example.test/one\nhttps://example.test/two\nhttps://example.test/one");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void KeepsUrlsWhoseCaseSensitiveIdsDiffer()
    {
        var result = UrlInputParser.Parse("https://youtu.be/abcDEF\nhttps://youtu.be/ABCdef");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void TreatsSchemeAndHostCaseAsDuplicates()
    {
        var result = UrlInputParser.Parse("https://Example.TEST/Video\nHTTPS://example.test/Video");
        Assert.Single(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("file:///C:/secret.txt")]
    public void RejectsInvalidUrlInput(string value)
    {
        Assert.Throws<FormatException>(() => UrlInputParser.Parse(value));
    }

    [Fact]
    public void AllowsExpectedDownloadStateTransitions()
    {
        Assert.True(DownloadStateMachine.CanTransition(DownloadState.Queued, DownloadState.Preparing));
        Assert.True(DownloadStateMachine.CanTransition(DownloadState.Downloading, DownloadState.Processing));
        Assert.True(DownloadStateMachine.CanTransition(DownloadState.Failed, DownloadState.Queued));
        Assert.False(DownloadStateMachine.CanTransition(DownloadState.Completed, DownloadState.Downloading));
    }

    [Fact]
    public void LogIsBounded()
    {
        var task = new DownloadTask(new DownloadRequest { Url = "https://example.test", OutputDirectory = "C:\\Media" });
        for (var index = 0; index < 5000; index++) task.AppendLog(new string('x', 100));
        Assert.True(task.LogText.Length < 300_000);
        Assert.Contains("Log truncated", task.LogText);
    }

    [Theory]
    [InlineData(DownloadMode.Video, "MP4 1080p")]
    [InlineData(DownloadMode.VideoOnly, "MP4 1080p")]
    [InlineData(DownloadMode.AudioOnly, "MP3 192K")]
    public void FormatSummaryDescribesTheRequestedOutput(DownloadMode mode, string expected)
    {
        var task = new DownloadTask(new DownloadRequest
        {
            Url = "https://example.test/watch",
            OutputDirectory = "C:\\Media",
            Mode = mode,
            VideoQuality = "1080p",
        });

        Assert.Equal(expected, task.FormatSummary);
    }

    [Theory]
    [InlineData("https://www.YouTube.com/watch?v=abc", "youtube.com")]
    [InlineData("https://vimeo.com/123", "vimeo.com")]
    [InlineData("not a url", "")]
    public void SiteHostShowsTheLinksHostWithoutWww(string url, string expected)
    {
        var task = new DownloadTask(new DownloadRequest { Url = url, OutputDirectory = "C:\\Media" });

        Assert.Equal(expected, task.SiteHost);
    }
}
