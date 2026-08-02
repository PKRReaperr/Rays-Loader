using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RaysLoader.Models;
using RaysLoader.Services;

namespace RaysLoader.ViewModels;

public enum DownloadFilter
{
    All,
    Running,
    Queued,
    Completed,
    Failed,
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDownloadQueueService _queue;
    private readonly ObservableCollection<DownloadTask> _visibleDownloads = [];
    private readonly ReadOnlyObservableCollection<DownloadTask> _readOnlyVisibleDownloads;

    [ObservableProperty]
    private DownloadFilter selectedFilter;

    [ObservableProperty]
    private bool stopAllRemaining;

    public MainViewModel(IDownloadQueueService queue)
    {
        _queue = queue;
        _readOnlyVisibleDownloads = new ReadOnlyObservableCollection<DownloadTask>(_visibleDownloads);
        ((INotifyCollectionChanged)_queue.Downloads).CollectionChanged += DownloadsCollectionChanged;
        foreach (var task in _queue.Downloads) task.PropertyChanged += TaskPropertyChanged;
        Refresh();
    }

    public ReadOnlyObservableCollection<DownloadTask> VisibleDownloads => _readOnlyVisibleDownloads;
    public int AllCount => _queue.Downloads.Count;
    public int RunningCount => _queue.Downloads.Count(IsRunning);
    public int QueuedCount => _queue.Downloads.Count(task => task.State is DownloadState.Queued);
    public int CompletedCount => _queue.Downloads.Count(task => task.State is DownloadState.Completed);
    public int FailedCount => _queue.Downloads.Count(task => task.State is DownloadState.Failed);
    public bool HasVisibleDownloads => _visibleDownloads.Count > 0;
    public string EmptyTitle => SelectedFilter is DownloadFilter.All ? "No downloads yet" : $"No {SelectedFilter.ToString().ToLowerInvariant()} downloads";
    public string EmptyDescription => SelectedFilter is DownloadFilter.All ? "Add a link to start downloading." : "Downloads in this state will appear here.";

    public event EventHandler? AddDownloadRequested;
    public event EventHandler<DownloadTask>? LogRequested;

    partial void OnSelectedFilterChanged(DownloadFilter value) => Refresh();
    partial void OnStopAllRemainingChanged(bool value)
    {
        if (value) _queue.StopAll();
        else _queue.StopAllRemaining = false;
    }

    [RelayCommand]
    private void AddDownload() => AddDownloadRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Stop(DownloadTask task) => _queue.Stop(task);

    [RelayCommand]
    private void Retry(DownloadTask task) => _queue.Retry(task);

    [RelayCommand]
    private void RetryAllFailed() => _queue.RetryAllFailed();

    [RelayCommand(CanExecute = nameof(CanStartQueue))]
    private void StartQueue()
    {
        StopAllRemaining = false;
        _queue.StartQueued();
    }

    private bool CanStartQueue() => QueuedCount > 0;

    [RelayCommand]
    private void StopAll() => _queue.StopAll();

    [RelayCommand]
    private void ClearCompleted() => _queue.ClearCompleted();

    [RelayCommand]
    private void ClearFailed() => _queue.ClearFailed();

    [RelayCommand]
    private void Remove(DownloadTask task) => _queue.Remove(task);

    [RelayCommand]
    private void ShowLog(DownloadTask task) => LogRequested?.Invoke(this, task);

    [RelayCommand]
    private static void OpenFile(DownloadTask task)
    {
        if (!task.CanOpenFile) return;
        var outputFilename = ResolveOutputFilename(task);
        if (!File.Exists(outputFilename)) return;
        Process.Start(new ProcessStartInfo(outputFilename) { UseShellExecute = true });
    }

    [RelayCommand]
    private static void OpenFolder(DownloadTask task)
    {
        var outputFilename = ResolveOutputFilename(task);
        if (File.Exists(outputFilename))
        {
            // Explorer's /select switch requires its legacy comma-and-quoted-path syntax.
            Process.Start(new ProcessStartInfo("explorer.exe")
            {
                Arguments = $"/select,\"{outputFilename}\"",
                UseShellExecute = true,
            });
            return;
        }

        var outputDirectory = Path.GetFullPath(task.OutputDirectory);
        if (Directory.Exists(outputDirectory))
        {
            Process.Start(new ProcessStartInfo(outputDirectory) { UseShellExecute = true });
        }
    }

    private static string ResolveOutputFilename(DownloadTask task)
    {
        if (string.IsNullOrWhiteSpace(task.OutputFilename)) return string.Empty;
        var outputDirectory = Path.GetFullPath(task.OutputDirectory);
        return Path.IsPathFullyQualified(task.OutputFilename)
            ? Path.GetFullPath(task.OutputFilename)
            : Path.GetFullPath(task.OutputFilename, outputDirectory);
    }

    private void DownloadsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (eventArgs.OldItems is not null)
        {
            foreach (DownloadTask task in eventArgs.OldItems) task.PropertyChanged -= TaskPropertyChanged;
        }
        if (eventArgs.NewItems is not null)
        {
            foreach (DownloadTask task in eventArgs.NewItems) task.PropertyChanged += TaskPropertyChanged;
        }
        Refresh();
    }

    private void TaskPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(DownloadTask.State)) Refresh();
    }

    private void Refresh()
    {
        var matching = _queue.Downloads.Where(MatchesFilter).OrderByDescending(task => task.CreatedAt).ToArray();
        _visibleDownloads.Clear();
        foreach (var task in matching) _visibleDownloads.Add(task);
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(QueuedCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(HasVisibleDownloads));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
        StartQueueCommand.NotifyCanExecuteChanged();
    }

    private bool MatchesFilter(DownloadTask task) => SelectedFilter switch
    {
        DownloadFilter.All => true,
        DownloadFilter.Running => IsRunning(task),
        DownloadFilter.Queued => task.State is DownloadState.Queued,
        DownloadFilter.Completed => task.State is DownloadState.Completed,
        DownloadFilter.Failed => task.State is DownloadState.Failed,
        _ => true,
    };

    private static bool IsRunning(DownloadTask task) => task.State is DownloadState.Preparing or DownloadState.Analyzing or DownloadState.Downloading or DownloadState.Processing;
}
