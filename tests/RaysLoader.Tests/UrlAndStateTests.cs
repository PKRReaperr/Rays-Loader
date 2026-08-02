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
}
