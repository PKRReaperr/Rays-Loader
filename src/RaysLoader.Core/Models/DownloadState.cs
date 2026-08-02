namespace RaysLoader.Models;

public enum DownloadState
{
    Queued,
    Preparing,
    Analyzing,
    Downloading,
    Processing,
    Paused,
    Completed,
    Failed,
    Cancelled,
}
