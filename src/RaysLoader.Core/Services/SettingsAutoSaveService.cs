using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RaysLoader.Settings;

namespace RaysLoader.Services;

/// <summary>
/// Opt-in persistence for a settings editor. Create after loading settings, then
/// await <see cref="FlushAsync"/> before leaving the editor or shutting down.
/// </summary>
public sealed class SettingsAutoSaveService : ObservableObject, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly TimeSpan _debounceDelay;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private AppSettings _observedSettings;
    private CancellationTokenSource? _debounceCancellation;
    private long _revision;
    private long _savedRevision;
    private bool _isSaving;
    private bool _disposed;
    private Exception? _lastError;

    public SettingsAutoSaveService(ISettingsService settingsService, TimeSpan? debounceDelay = null)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        _settingsService = settingsService;
        _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(500);
        if (_debounceDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(debounceDelay));
        _observedSettings = settingsService.Current;
        _observedSettings.PropertyChanged += OnSettingsChanged;
        if (settingsService is ISettingsChangeNotifier notifier) notifier.CurrentChanged += OnCurrentChanged;
    }

    public bool IsSaving { get { lock (_sync) return _isSaving; } }
    public bool HasPendingChanges { get { lock (_sync) return _revision != _savedRevision; } }
    public Exception? LastError { get { lock (_sync) return _lastError; } }
    public string StatusMessage
    {
        get
        {
            lock (_sync)
            {
                if (_lastError is not null) return $"Couldn't save settings: {_lastError.Message}";
                if (_isSaving) return "Saving changes…";
                return _revision != _savedRevision ? "Changes will save automatically…" : "All changes saved";
            }
        }
    }

    /// <summary>
    /// Saves all edits, including changes made while a write is in progress.
    /// Returns false on a persistence failure, leaving the edits pending for retry.
    /// </summary>
    public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CancelDebounce();
        }

        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                long savingRevision;
                AppSettings settings;
                lock (_sync)
                {
                    if (_disposed || _revision == _savedRevision) return _lastError is null;
                    settings = _settingsService.Current;
                    savingRevision = _revision;
                    _isSaving = true;
                    _lastError = null;
                }
                NotifyStatusChanged();

                try
                {
                    await _settingsService.SaveAsync(settings, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    lock (_sync) _isSaving = false;
                    NotifyStatusChanged();
                    throw;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or ArgumentException or System.Text.Json.JsonException)
                {
                    lock (_sync)
                    {
                        _isSaving = false;
                        _lastError = exception;
                    }
                    NotifyStatusChanged();
                    return false;
                }

                lock (_sync)
                {
                    _savedRevision = savingRevision;
                    _isSaving = false;
                }
                NotifyStatusChanged();
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args) => OnContext(() =>
    {
        // A queued notification from the previous object must not dirty a reset.
        if (ReferenceEquals(sender, _observedSettings)) ScheduleSave();
    });

    private void OnCurrentChanged(object? sender, EventArgs args) => OnContext(() =>
    {
        lock (_sync)
        {
            if (_disposed || ReferenceEquals(_observedSettings, _settingsService.Current)) return;
            _observedSettings.PropertyChanged -= OnSettingsChanged;
            _observedSettings = _settingsService.Current;
            _observedSettings.PropertyChanged += OnSettingsChanged;
        }
        ScheduleSave();
    });

    private void ScheduleSave()
    {
        CancellationToken cancellationToken;
        lock (_sync)
        {
            if (_disposed) return;
            _revision++;
            _lastError = null;
            CancelDebounce();
            _debounceCancellation = new CancellationTokenSource();
            cancellationToken = _debounceCancellation.Token;
        }
        NotifyStatusChanged();
        _ = SaveAfterDelayAsync(cancellationToken);
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_debounceDelay, cancellationToken);
            // Debouncing never cancels a write that has already started.
            await FlushAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) { }
    }

    private void CancelDebounce()
    {
        _debounceCancellation?.Cancel();
        _debounceCancellation?.Dispose();
        _debounceCancellation = null;
    }

    private void NotifyStatusChanged() => OnContext(() =>
    {
        OnPropertyChanged(nameof(IsSaving));
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(LastError));
        OnPropertyChanged(nameof(StatusMessage));
    });

    private void OnContext(Action action)
    {
        if (_context is null || ReferenceEquals(_context, SynchronizationContext.Current)) action();
        else _context.Post(_ => action(), null);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            CancelDebounce();
            _observedSettings.PropertyChanged -= OnSettingsChanged;
            if (_settingsService is ISettingsChangeNotifier notifier) notifier.CurrentChanged -= OnCurrentChanged;
        }
    }
}
