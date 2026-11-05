namespace RaysLoader.Services;

/// <summary>
/// An async slot limiter whose limit can change while waiters are pending.
/// Lowering the limit never interrupts work that already holds a slot; it only
/// delays new acquisitions until enough slots have been released.
/// </summary>
internal sealed class ConcurrencyGate
{
    private readonly object _sync = new();
    private readonly List<TaskCompletionSource> _waiters = [];
    private int _limit;
    private int _inUse;

    public ConcurrencyGate(int limit) => _limit = Math.Max(1, limit);

    public int Limit { get { lock (_sync) return _limit; } }
    public int InUse { get { lock (_sync) return _inUse; } }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TaskCompletionSource waiter;
            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_inUse < _limit)
                {
                    _inUse++;
                    return;
                }

                waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add(waiter);
            }

            try
            {
                await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (_sync) _waiters.Remove(waiter);
            }
        }
    }

    public void Release()
    {
        lock (_sync)
        {
            if (_inUse > 0) _inUse--;
            SignalWaiters();
        }
    }

    public void SetLimit(int limit)
    {
        lock (_sync)
        {
            _limit = Math.Max(1, limit);
            SignalWaiters();
        }
    }

    // Waiters re-check the limit themselves, so waking all of them is safe.
    private void SignalWaiters()
    {
        foreach (var waiter in _waiters) waiter.TrySetResult();
        _waiters.Clear();
    }
}
