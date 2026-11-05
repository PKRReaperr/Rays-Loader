using System.Text.Json;
using RaysLoader.Services;
using RaysLoader.Settings;

namespace RaysLoader.Tests;

public sealed class SettingsAutoSaveServiceTests
{
    [Fact]
    public async Task RapidTextEditsAreCoalescedAndSavedWithoutExplicitSave()
    {
        var settings = new RecordingSettingsService();
        using var autoSave = new SettingsAutoSaveService(settings, TimeSpan.FromMilliseconds(60));

        settings.Current.FilenameTemplate = "first";
        settings.Current.FilenameTemplate = "second";
        settings.Current.FilenameTemplate = "final";

        Assert.True(autoSave.HasPendingChanges);
        Assert.Empty(settings.Written);
        await settings.FirstSave.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await autoSave.FlushAsync());
        Assert.Equal("final", Assert.Single(settings.Written).FilenameTemplate);
        Assert.False(autoSave.HasPendingChanges);
        Assert.Equal("All changes saved", autoSave.StatusMessage);
    }

    [Fact]
    public async Task FlushPersistsChangesMadeDuringASlowSaveAndSerializesCompetingFlushes()
    {
        var settings = new RecordingSettingsService();
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        settings.BeforeFirstSaveCompletes = () => releaseWrite.Task;
        using var autoSave = new SettingsAutoSaveService(settings, TimeSpan.FromMinutes(1));
        settings.Current.FilenameTemplate = "before";

        var firstFlush = autoSave.FlushAsync();
        await settings.FirstSave.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(autoSave.IsSaving);
        settings.Current.FilenameTemplate = "latest";
        var secondFlush = autoSave.FlushAsync();
        releaseWrite.SetResult();

        Assert.All(await Task.WhenAll(firstFlush, secondFlush), Assert.True);
        Assert.Equal(["before", "latest"], settings.Written.Select(value => value.FilenameTemplate));
        Assert.Equal(1, settings.MaximumConcurrentSaves);
        Assert.False(autoSave.HasPendingChanges);
    }

    [Fact]
    public async Task SaveFailuresKeepChangesPendingAndCanBeRetried()
    {
        var settings = new RecordingSettingsService { Failure = new IOException("Disk is unavailable") };
        using var autoSave = new SettingsAutoSaveService(settings, TimeSpan.FromMinutes(1));
        settings.Current.DefaultContainer = "mkv";

        Assert.False(await autoSave.FlushAsync());
        Assert.True(autoSave.HasPendingChanges);
        Assert.False(autoSave.IsSaving);
        Assert.IsType<IOException>(autoSave.LastError);
        Assert.Contains("Disk is unavailable", autoSave.StatusMessage);

        settings.Failure = null;
        Assert.True(await autoSave.FlushAsync());
        Assert.Null(autoSave.LastError);
        Assert.False(autoSave.HasPendingChanges);
        Assert.Equal("mkv", Assert.Single(settings.Written).DefaultContainer);
    }

    [Fact]
    public async Task ResetDetachesOldSettingsAndContinuesWatchingTheReplacement()
    {
        var settings = new RecordingSettingsService();
        using var autoSave = new SettingsAutoSaveService(settings, TimeSpan.FromMinutes(1));
        var previous = settings.Current;
        previous.DefaultContainer = "mkv";
        await settings.ResetAsync();
        settings.Current.DefaultContainer = "webm";

        Assert.True(await autoSave.FlushAsync());
        Assert.Equal("webm", Assert.Single(settings.Written).DefaultContainer);
        previous.DefaultContainer = "avi";
        Assert.False(autoSave.HasPendingChanges);

        settings.Current.DefaultContainer = "mp4";
        Assert.True(await autoSave.FlushAsync());
        Assert.Equal("mp4", settings.Written[^1].DefaultContainer);
    }

    [Fact]
    public async Task ResetDuringAnInFlightSavePersistsTheReplacementAndItsLatestEdits()
    {
        var settings = new RecordingSettingsService();
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        settings.BeforeFirstSaveCompletes = () => releaseWrite.Task;
        using var autoSave = new SettingsAutoSaveService(settings, TimeSpan.FromMinutes(1));
        settings.Current.DefaultContainer = "mkv";
        var flushing = autoSave.FlushAsync();
        await settings.FirstSave.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await settings.ResetAsync();
        settings.Current.DefaultContainer = "webm";
        releaseWrite.SetResult();

        Assert.True(await flushing);
        Assert.Equal("webm", settings.Written[^1].DefaultContainer);
        Assert.False(autoSave.HasPendingChanges);
    }

    [Fact]
    public async Task DisposingStopsObservationAndDoesNotWriteUnrequestedChanges()
    {
        var settings = new RecordingSettingsService();
        var autoSave = new SettingsAutoSaveService(settings, TimeSpan.FromMinutes(1));
        Assert.True(await autoSave.FlushAsync());
        Assert.Empty(settings.Written);
        autoSave.Dispose();

        settings.Current.Proxy = "http://localhost:8080";
        Assert.False(autoSave.HasPendingChanges);
        Assert.Empty(settings.Written);
    }

    [Fact]
    public async Task ChangesAndStatusNotificationsReturnToTheCapturedUiContext()
    {
        var settings = new RecordingSettingsService();
        var uiContext = new QueuedSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        SettingsAutoSaveService autoSave;
        SynchronizationContext.SetSynchronizationContext(uiContext);
        try
        {
            autoSave = new SettingsAutoSaveService(settings, TimeSpan.FromMinutes(1));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
        using (autoSave)
        {
            var notifiedContexts = new List<SynchronizationContext?>();
            autoSave.PropertyChanged += (_, _) => notifiedContexts.Add(SynchronizationContext.Current);
            await Task.Run(() => settings.Current.DefaultContainer = "mkv");
            Assert.Empty(notifiedContexts);

            uiContext.DeliverPostedCallbacks();
            Assert.True(autoSave.HasPendingChanges);
            Assert.True(await autoSave.FlushAsync());
            uiContext.DeliverPostedCallbacks();

            Assert.NotEmpty(notifiedContexts);
            Assert.All(notifiedContexts, context => Assert.Same(uiContext, context));
            Assert.False(autoSave.HasPendingChanges);
        }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _callbacks = new();
        public override void Post(SendOrPostCallback callback, object? state) => _callbacks.Enqueue(() => callback(state));

        public void DeliverPostedCallbacks()
        {
            var previousContext = Current;
            SetSynchronizationContext(this);
            try
            {
                while (_callbacks.TryDequeue(out var callback)) callback();
            }
            finally
            {
                SetSynchronizationContext(previousContext);
            }
        }
    }

    private sealed class RecordingSettingsService : ISettingsService, ISettingsChangeNotifier
    {
        private int _activeSaves;
        private int _saveCalls;
        public AppSettings Current { get; private set; } = new();
        public string SettingsDirectory => "unused";
        public event EventHandler? CurrentChanged;
        public List<AppSettings> Written { get; } = [];
        public TaskCompletionSource FirstSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<Task>? BeforeFirstSaveCompletes { get; set; }
        public Exception? Failure { get; set; }
        public int MaximumConcurrentSaves { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

        public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            MaximumConcurrentSaves = Math.Max(MaximumConcurrentSaves, Interlocked.Increment(ref _activeSaves));
            try
            {
                if (Failure is not null) throw Failure;
                var snapshot = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
                if (Interlocked.Increment(ref _saveCalls) == 1)
                {
                    FirstSave.TrySetResult();
                    if (BeforeFirstSaveCompletes is not null) await BeforeFirstSaveCompletes();
                }
                Written.Add(snapshot);
            }
            finally
            {
                Interlocked.Decrement(ref _activeSaves);
            }
        }

        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            Current = new AppSettings();
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }
}
