using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Channels;
using RaysLoader.Models;
using RaysLoader.Settings;

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
    private readonly ConcurrencyGate _slots;
    private readonly IDownloadExecutor _executor;
    private readonly IUiDispatcher _dispatcher;
    private readonly ISettingsService _settingsService;
    private readonly object _settingsSync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _dispatcherTask;
    private AppSettings _observedSettings;
    private volatile bool _stopAllRemaining;

    public DownloadQueueService(IDownloadExecutor executor, ISettingsService settingsService, IUiDispatcher dispatcher)
    {
        _executor = executor;
        _dispatcher = dispatcher;
        _settingsService = settingsService;
        _observedSettings = settingsService.Current;
        _slots = new ConcurrencyGate(ClampConcurrency(_observedSettings.MaximumSimultaneousDownloads));
        // The concurrency limit follows the live settings object, so changes from
        // onboarding or Settings apply to the next download without a restart.
        _observedSettings.PropertyChanged += OnSettingsPropertyChanged;
        if (settingsService is ISettingsChangeNotifier notifier) notifier.CurrentChanged += OnSettingsReplaced;
        Downloads = new ReadOnlyObservableCollection<DownloadTask>(_downloads);
        _dispatcherTask = DispatchAsync(_lifetime.Token);
    }

    public ReadOnlyObservableCollection<DownloadTask> Downloads { get; }
    public bool StopAllRemaining { get => _stopAllRemaining; set => _stopAllRemaining = value; }
    internal int ConcurrencyLimit => _slots.Limit;

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
            // The dispatcher may have claimed a slot for this task between the
            // check above and the state change; cancel that start as well.
            if (_active.TryGetValue(task.Id, out cancellation)) TryCancel(cancellation);
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
        lock (_settingsSync) _observedSettings.PropertyChanged -= OnSettingsPropertyChanged;
        if (_settingsService is ISettingsChangeNotifier notifier) notifier.CurrentChanged -= OnSettingsReplaced;
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
            if (!IsRunnable(task))
            {
                _scheduled.TryRemove(task.Id, out _);
                continue;
            }

            await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            var taskCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _active[task.Id] = taskCancellation;

            // Waiting for a slot can take as long as another download. Re-check after
            // registering the cancellation so a Stop, Remove, or Stop All issued while
            // this task waited is honoured instead of starting a cancelled download.
            if (!IsRunnable(task))
            {
                _active.TryRemove(task.Id, out _);
                _scheduled.TryRemove(task.Id, out _);
                taskCancellation.Dispose();
                _slots.Release();
                continue;
            }

            _ = RunTaskAsync(task, taskCancellation);
        }
    }

    private bool IsRunnable(DownloadTask task) =>
        !StopAllRemaining && task.State is DownloadState.Queued && _known.ContainsKey(task.Id);

    private async Task RunTaskAsync(DownloadTask task, CancellationTokenSource taskCancellation)
    {
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
            taskCancellation.Dispose();
            _slots.Release();
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AppSettings.MaximumSimultaneousDownloads) && sender is AppSettings settings)
            _slots.SetLimit(ClampConcurrency(settings.MaximumSimultaneousDownloads));
    }

    private void OnSettingsReplaced(object? sender, EventArgs args)
    {
        AppSettings current;
        lock (_settingsSync)
        {
            current = _settingsService.Current;
            if (ReferenceEquals(current, _observedSettings)) return;
            _observedSettings.PropertyChanged -= OnSettingsPropertyChanged;
            _observedSettings = current;
            _observedSettings.PropertyChanged += OnSettingsPropertyChanged;
        }
        _slots.SetLimit(ClampConcurrency(current.MaximumSimultaneousDownloads));
    }

    private static int ClampConcurrency(int value) => Math.Clamp(value, 1, 8);
}
