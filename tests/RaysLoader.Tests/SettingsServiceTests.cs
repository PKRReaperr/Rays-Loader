using RaysLoader.Services;
using RaysLoader.Settings;

namespace RaysLoader.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RaysLoader.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RoundTripsAndNormalizesSettings()
    {
        var service = new SettingsService(_directory);
        var settings = new AppSettings
        {
            Theme = AppTheme.Dark,
            AccentColor = AppAccent.Coral,
            MaximumSimultaneousDownloads = 99,
            DefaultContainer = "mkv",
            AuthenticationMode = AuthenticationMode.Browser,
            BrowserCookies = "FIREFOX",
            YtDlpUpdateChannel = YtDlpUpdateChannel.Stable,
        };
        await service.SaveAsync(settings);

        var reloaded = await new SettingsService(_directory).LoadAsync();

        Assert.Equal(AppTheme.Dark, reloaded.Theme);
        Assert.Equal(AppAccent.Coral, reloaded.AccentColor);
        Assert.Equal(8, reloaded.MaximumSimultaneousDownloads);
        Assert.Equal("mkv", reloaded.DefaultContainer);
        Assert.Equal(AuthenticationMode.Browser, reloaded.AuthenticationMode);
        Assert.Equal("firefox", reloaded.BrowserCookies);
        Assert.Equal(YtDlpUpdateChannel.Stable, reloaded.YtDlpUpdateChannel);
    }

    [Fact]
    public async Task BrowserAuthenticationWithoutSavedBrowserDefaultsToFirefox()
    {
        var service = new SettingsService(_directory);

        await service.SaveAsync(new AppSettings { AuthenticationMode = AuthenticationMode.Browser });

        Assert.Equal("firefox", service.Current.BrowserCookies);
    }

    [Fact]
    public async Task CorruptSettingsFallBackWithoutCrashing()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"), "{ definitely not json");

        var settings = await new SettingsService(_directory).LoadAsync();

        Assert.Equal(2, settings.MaximumSimultaneousDownloads);
        Assert.Single(Directory.GetFiles(_directory, "settings.corrupt-*.json"));
    }

    [Fact]
    public void NewSettingsStartInCurrentUsersDownloadsAndPendingOnboarding()
    {
        var settings = new AppSettings();

        Assert.Equal(AppPaths.DefaultOutputDirectory, settings.DefaultOutputDirectory);
        Assert.False(settings.HasCompletedOnboarding);
        Assert.True(settings.AutomaticallyUpdateYtDlp);
        Assert.Equal(YtDlpUpdateChannel.Nightly, settings.YtDlpUpdateChannel);
    }

    [Fact]
    public async Task MissingOutputSettingDefaultsToCurrentUsersDownloads()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"), "{}");

        var settings = await new SettingsService(_directory).LoadAsync();

        Assert.Equal(AppPaths.DefaultOutputDirectory, settings.DefaultOutputDirectory);
    }

    [Fact]
    public async Task ConventionalDownloadsPathIsStoredAsReadableAbsolutePath()
    {
        var service = new SettingsService(_directory);
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var settings = new AppSettings
        {
            DefaultOutputDirectory = downloads,
        };

        await service.SaveAsync(settings);

        Assert.Equal(Path.GetFullPath(downloads), service.Current.DefaultOutputDirectory);
    }

    [Fact]
    public async Task ExistingSettingsWithoutOnboardingFlagAreMigratedWithoutPrompt()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "settings.json"),
            """{"DefaultOutputDirectory":"%USERPROFILE%\\Downloads"}""");

        var settings = await new SettingsService(_directory).LoadAsync();

        Assert.True(settings.HasCompletedOnboarding);
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            settings.DefaultOutputDirectory);
    }

    [Fact]
    public async Task InvalidSavedOutputPathFallsBackWithoutCrashing()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "settings.json"),
            """{"DefaultOutputDirectory":"%RAYS_LOADER_UNKNOWN%\\Downloads"}""");

        var settings = await new SettingsService(_directory).LoadAsync();

        Assert.Equal(AppPaths.DefaultOutputDirectory, settings.DefaultOutputDirectory);
        Assert.Single(Directory.GetFiles(_directory, "settings.corrupt-*.json"));
    }

    [Fact]
    public async Task SeparateProfilesCanOptOutOfLegacySettingsImport()
    {
        var service = new SettingsService(_directory, importLegacySettings: false);

        var settings = await service.LoadAsync();

        Assert.False(settings.HasCompletedOnboarding);
        Assert.Equal(AppPaths.DefaultOutputDirectory, settings.DefaultOutputDirectory);
        Assert.True(File.Exists(Path.Combine(_directory, "settings.json")));
    }

    [Fact]
    public async Task ConcurrentSavesUseOneAtomicWriterAndKeepTheLatestSettings()
    {
        var service = new SettingsService(_directory, importLegacySettings: false);
        var settings = Enumerable.Range(0, 12)
            .Select(index => new AppSettings { FilenameTemplate = $"template-{index}" })
            .ToArray();

        await Task.WhenAll(settings.Select(value => service.SaveAsync(value)));

        Assert.Same(settings[^1], service.Current);
        var reloaded = await new SettingsService(_directory, importLegacySettings: false).LoadAsync();
        Assert.Equal(settings[^1].FilenameTemplate, reloaded.FilenameTemplate);
        Assert.False(File.Exists(Path.Combine(_directory, "settings.json.tmp")));
    }

    [Fact]
    public async Task ResetNotifiesSubscribersOfTheNewCurrentSettings()
    {
        var service = new SettingsService(_directory, importLegacySettings: false);
        await service.SaveAsync(new AppSettings { HasCompletedOnboarding = true, DefaultContainer = "mkv" });
        var original = service.Current;
        var changes = new List<AppSettings>();
        service.CurrentChanged += (_, _) => changes.Add(service.Current);

        await service.ResetAsync();

        Assert.NotSame(original, service.Current);
        Assert.Same(service.Current, Assert.Single(changes));
        Assert.True(service.Current.HasCompletedOnboarding);
        Assert.Equal("mp4", service.Current.DefaultContainer);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
