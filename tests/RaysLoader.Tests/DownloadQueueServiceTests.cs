using RaysLoader.Models;
using RaysLoader.Services;

namespace RaysLoader.Tests;

public sealed class DownloadQueueServiceTests
{
    [Fact]
    public async Task EnforcesConfiguredConcurrencyAndPreventsDuplicateStarts()
    {
        var executor = new BlockingExecutor();
        var settings = new StubSettingsService(2);
        await using var queue = new DownloadQueueService(executor, settings, new ImmediateUiDispatcher());
        var request = new DownloadRequest { Url = "https://example.test/video", OutputDirectory = "C:\\Media" };

        var first = queue.Enqueue(request with { Url = "https://example.test/1" });
        queue.Enqueue(request with { Url = "https://example.test/2" });
        queue.Enqueue(request with { Url = "https://example.test/3" });
        await executor.WaitForStartsAsync(2);

        Assert.Equal(2, executor.MaximumConcurrent);
        Assert.Equal(1, executor.StartCounts[first.Id]);
        executor.ReleaseAll();
        await executor.WaitForStartsAsync(3);
    }

    [Fact]
    public async Task RetriesFailedTask()
    {
        var executor = new FailOnceExecutor();
        await using var queue = new DownloadQueueService(executor, new StubSettingsService(1), new ImmediateUiDispatcher());
        var task = queue.Enqueue(new DownloadRequest { Url = "https://example.test/fail", OutputDirectory = "C:\\Media" });
        await WaitUntilAsync(() => task.State is DownloadState.Failed);

        queue.Retry(task);
        await WaitUntilAsync(() => task.State is DownloadState.Completed);

        Assert.Equal(2, executor.Attempts);
    }

    [Fact]
    public async Task DeferredDownloadsWaitUntilQueueIsStarted()
    {
        var executor = new BlockingExecutor();
        await using var queue = new DownloadQueueService(executor, new StubSettingsService(2), new ImmediateUiDispatcher());
        var request = new DownloadRequest { Url = "https://example.test/video", OutputDirectory = "C:\\Media" };
        var first = queue.Enqueue(request with { Url = "https://example.test/1" }, startImmediately: false);
        var second = queue.Enqueue(request with { Url = "https://example.test/2" }, startImmediately: false);

        await Task.Delay(100);
        Assert.Equal(DownloadState.Queued, first.State);
        Assert.Equal(DownloadState.Queued, second.State);
        Assert.Empty(executor.StartCounts);

        queue.StartQueued();
        await executor.WaitForStartsAsync(2);
        Assert.Equal(1, executor.StartCounts[first.Id]);
        Assert.Equal(1, executor.StartCounts[second.Id]);
        executor.ReleaseAll();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class BlockingExecutor : IDownloadExecutor
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _started;
        public int MaximumConcurrent { get; private set; }
        public Dictionary<Guid, int> StartCounts { get; } = [];

        public async Task ExecuteAsync(DownloadTask task, CancellationToken cancellationToken)
        {
            lock (StartCounts)
            {
                StartCounts[task.Id] = StartCounts.GetValueOrDefault(task.Id) + 1;
                _started++;
                _active++;
                MaximumConcurrent = Math.Max(MaximumConcurrent, _active);
            }

            await _release.Task.WaitAsync(cancellationToken);
            task.State = DownloadState.Completed;
            lock (StartCounts) _active--;
        }

        public async Task WaitForStartsAsync(int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Volatile.Read(ref _started) < count) await Task.Delay(10, timeout.Token);
        }

        public void ReleaseAll() => _release.TrySetResult();
    }

    private sealed class FailOnceExecutor : IDownloadExecutor
    {
        public int Attempts { get; private set; }
        public Task ExecuteAsync(DownloadTask task, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Attempts == 1) throw new InvalidOperationException("Synthetic failure");
            task.State = DownloadState.Completed;
            return Task.CompletedTask;
        }
    }

    private sealed class StubSettingsService(int concurrency) : ISettingsService
    {
        public RaysLoader.Settings.AppSettings Current { get; } = new() { MaximumSimultaneousDownloads = concurrency };
        public string SettingsDirectory => Path.GetTempPath();
        public Task<RaysLoader.Settings.AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task SaveAsync(RaysLoader.Settings.AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
