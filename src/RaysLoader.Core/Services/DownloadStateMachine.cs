using RaysLoader.Models;

namespace RaysLoader.Services;

public static class DownloadStateMachine
{
    public static bool CanTransition(DownloadState from, DownloadState to) => from == to || (from, to) switch
    {
        (DownloadState.Queued, DownloadState.Preparing or DownloadState.Cancelled) => true,
        (DownloadState.Preparing, DownloadState.Analyzing or DownloadState.Downloading or DownloadState.Failed or DownloadState.Cancelled) => true,
        (DownloadState.Analyzing, DownloadState.Queued or DownloadState.Downloading or DownloadState.Failed or DownloadState.Cancelled) => true,
        (DownloadState.Downloading, DownloadState.Processing or DownloadState.Paused or DownloadState.Completed or DownloadState.Failed or DownloadState.Cancelled) => true,
        (DownloadState.Processing, DownloadState.Completed or DownloadState.Failed or DownloadState.Cancelled) => true,
        (DownloadState.Paused, DownloadState.Queued or DownloadState.Cancelled) => true,
        (DownloadState.Failed, DownloadState.Queued) => true,
        (DownloadState.Cancelled, DownloadState.Queued) => true,
        _ => false,
    };
}
