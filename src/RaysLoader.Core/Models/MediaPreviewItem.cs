using CommunityToolkit.Mvvm.ComponentModel;

namespace RaysLoader.Models;

public enum MediaPreviewState
{
    Loading,
    Ready,
    Failed,
}

public sealed partial class MediaPreviewItem(string url) : ObservableObject
{
    public string Url { get; } = url;
    [ObservableProperty] private string title = "Reading media details...";
    [ObservableProperty] private string thumbnailUrl = string.Empty;
    [ObservableProperty] private string source = "Connecting to source";
    [ObservableProperty] private string message = string.Empty;
    [ObservableProperty] private MediaPreviewState state = MediaPreviewState.Loading;

    public bool IsLoading => State is MediaPreviewState.Loading;
    public bool IsReady => State is MediaPreviewState.Ready;
    public bool HasFailed => State is MediaPreviewState.Failed;

    partial void OnStateChanged(MediaPreviewState value)
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(HasFailed));
    }
}
