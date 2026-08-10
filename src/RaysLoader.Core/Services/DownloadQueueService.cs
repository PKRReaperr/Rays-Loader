using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading.Channels;
using RaysLoader.Models;

namespace RaysLoader.Services;

public interface IDownloadExecutor
{
    Task ExecuteAsync(DownloadTask task, CancellationToken cancellationToken);
}

public sealed class DownloadExecutor(IYtDlpService ytDlpService) : IDownloadExecutor
{
    public Task ExecuteAsync(DownloadTask task, CancellationToken cancellationToken) => ytDlpService.ExecuteDownloadAsync(task, cancellationToken);
}

public interface IDownloadQueueService
{
    ReadOnlyObservableCollection<DownloadTask> Downloads { get; }
    bool StopAllRemaining { get; set; }
    DownloadTask Enqueue(DownloadRequest request, string? title = null, bool startImmediately = true);
    void StartQueued();
    void Stop(DownloadTask task);
    void StopAll();
    void Retry(DownloadTask task);
    void RetryAllFailed();
    void Remove(DownloadTask task);
    void ClearCompleted();
    void ClearFailed();
}

public sealed class DownloadQueueService : IDownloadQueueService, IAsyncDisposable
{
    private readonly ObservableCollection<DownloadTask> _downloads = [];
    private readonly Channel<DownloadTask> _pending = Channel.CreateUnbounded<DownloadTask>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _active = new();
    private readonly ConcurrentDictionary<Guid, byte> _scheduled = new();
    private readonly ConcurrentDictionary<Guid, byte> _known = new();
    private readonly SemaphoreSlim _slots;
    private readonly IDownloadExecutor _executor;
    private readonly IUiDispatcher _dispatcher;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _dispatcherTask;

    public DownloadQueueService(IDownloadExecutor executor, ISettingsService settingsService, IUiDispatcher dispatcher)
    {
        _executor = executor;
        _dispatcher = dispatcher;
        _slots = new SemaphoreSlim(Math.Clamp(settingsService.Current.MaximumSimultaneousDownloads, 1, 8));
        Downloads = new ReadOnlyObservableCollection<DownloadTask>(_downloads);
        _dispatcherTask = DispatchAsync(_lifetime.Token);
    }

    public ReadOnlyObservableCollection<DownloadTask> Downloads { get; }
    public bool StopAllRemaining { get; set; }

    public DownloadTask Enqueue(DownloadRequest request, string? title = null, bool startImmediately = true)
    {
        var task = new DownloadTask(request, title);
        _known[task.Id] = 0;
        _downloads.Add(task);
        if (startImmediately) Schedule(task);
        return task;
    }

    public void StartQueued()
    {
        StopAllRemaining = false;
        foreach (var task in _downloads.Where(item => item.State is DownloadState.Queued).ToArray())
        {
            Schedule(task);
        }
    }

    public void Stop(DownloadTask task)
    {
        if (_active.TryGetValue(task.Id, out var cancellation))
        {
            TryCancel(cancellation);
            return;
        }

        if (task.State is DownloadState.Queued)
        {
            task.State = DownloadState.Cancelled;
            task.StatusText = "Cancelled";
        }
    }

    public void StopAll()
    {
        StopAllRemaining = true;
        foreach (var cancellation in _active.Values)
        {
            TryCancel(cancellation);
        }

        foreach (var task in _downloads.Where(item => item.State is DownloadState.Queued))
        {
            task.State = DownloadState.Cancelled;
            task.StatusText = "Cancelled";
        }
    }

    public void Retry(DownloadTask task)
    {
        if (task.State is not (DownloadState.Failed or DownloadState.Cancelled))
        {
            return;
        }

        StopAllRemaining = false;
        task.ErrorMessage = string.Empty;
        task.ProgressPercent = 0;
        task.DownloadSpeed = string.Empty;
        task.Eta = string.Empty;
        task.CurrentOperation = string.Empty;
        task.State = DownloadState.Queued;
        task.StatusText = "Queued";
        Schedule(task);
    }

    public void RetryAllFailed()
    {
        foreach (var task in _downloads.Where(item => item.State is DownloadState.Failed).ToArray())
        {
            Retry(task);
        }
    }

    public void Remove(DownloadTask task)
    {
        if (task.CanStop)
        {
            Stop(task);
        }

        _known.TryRemove(task.Id, out _);
        _downloads.Remove(task);
    }

    public void ClearCompleted()
    {
        foreach (var task in _downloads.Where(item => item.State is DownloadState.Completed or DownloadState.Cancelled).ToArray())
        {
            _known.TryRemove(task.Id, out _);
            _downloads.Remove(task);
        }
    }

    public void ClearFailed()
    {
        foreach (var task in _downloads.Where(item => item.State is DownloadState.Failed).ToArray())
        {
            _known.TryRemove(task.Id, out _);
            _downloads.Remove(task);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        foreach (var cancellation in _active.Values)
        {
            TryCancel(cancellation);
        }

        try
        {
            await _dispatcherTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _lifetime.Dispose();
        _slots.Dispose();
    }

    private void Schedule(DownloadTask task)
    {
        if (!_scheduled.TryAdd(task.Id, 0))
        {
            return;
        }

        if (!_pending.Writer.TryWrite(task))
        {
            _scheduled.TryRemove(task.Id, out _);
            throw new InvalidOperationException("The download queue is not accepting new tasks.");
        }
    }

    private static void TryCancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A task can complete and dispose its linked token while shutdown
            // is enumerating the active-operation snapshot.
        }
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await foreach (var task in _pending.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (StopAllRemaining || task.State is not DownloadState.Queued || !_known.ContainsKey(task.Id))
            {
                _scheduled.TryRemove(task.Id, out _);
                continue;
            }

            await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            _ = RunTaskAsync(task, cancellationToken);
        }
    }

    private async Task RunTaskAsync(DownloadTask task, CancellationToken lifetimeToken)
    {
        using var taskCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        _active[task.Id] = taskCancellation;
        try
        {
            await _executor.ExecuteAsync(task, taskCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (taskCancellation.IsCancellationRequested)
        {
            _dispatcher.Enqueue(() =>
            {
                task.State = DownloadState.Cancelled;
                task.StatusText = "Cancelled";
            });
        }
        catch (Exception exception)
        {
            _dispatcher.Enqueue(() =>
            {
                task.State = DownloadState.Failed;
                task.StatusText = "Failed";
                task.ErrorMessage = exception.Message;
                task.AppendLog(exception.Message, isError: true);
            });
        }
        finally
        {
            _active.TryRemove(task.Id, out _);
            _scheduled.TryRemove(task.Id, out _);
            _slots.Release();
        }
    }
}
