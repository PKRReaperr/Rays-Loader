using RaysLoader.Services;

namespace RaysLoader.Tests;

public sealed class ProgressParserServiceTests
{
    private readonly ProgressParserService _parser = new();

    [Fact]
    public void ParsesCompleteMachineReadableProgress()
    {
        var parsed = _parser.TryParse("rays-progress: 77.35%|18.00MiB/s|00:39|81000000|104000000|4|8|2|10", out var result);

        Assert.True(parsed);
        Assert.Equal(77.35, result.Percent);
        Assert.Equal("18.00MiB/s", result.Speed);
        Assert.Equal("00:39", result.Eta);
        Assert.Equal(81_000_000, result.DownloadedBytes);
        Assert.Equal(4, result.FragmentIndex);
        Assert.Equal(10, result.PlaylistCount);
    }

    [Fact]
    public void HandlesUnknownAndMissingFields()
    {
        Assert.True(_parser.TryParse("rays-progress:NA|NA|NA||||||", out var result));
        Assert.Null(result.Percent);
        Assert.Empty(result.Speed);
        Assert.Null(result.TotalBytes);
    }

    [Fact]
    public void ParsesNormalYtDlpCommandLineProgress()
    {
        var parsed = _parser.TryParse("[download]  42.7% of   57.15GiB at   18.49MiB/s ETA 30:12", out var result);

        Assert.True(parsed);
        Assert.Equal(42.7, result.Percent);
        Assert.Equal("18.49MiB/s", result.Speed);
        Assert.Equal("30:12", result.Eta);
    }

    [Theory]
    [InlineData("[Merger] Merging formats into file", "Merging video and audio")]
    [InlineData("[ExtractAudio] Destination: audio.mp3", "Extracting audio")]
    [InlineData("[Metadata] Adding metadata", "Embedding metadata")]
    public void ParsesPostProcessingOperations(string line, string operation)
    {
        Assert.True(_parser.TryParse(line, out var result));
        Assert.Equal(operation, result.Operation);
    }

    [Fact]
    public void IgnoresUnexpectedOutput()
    {
        Assert.False(_parser.TryParse("an unrelated diagnostic line", out _));
    }
}
