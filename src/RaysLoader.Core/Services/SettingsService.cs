using System.Text.Json;
using RaysLoader.Models;
using RaysLoader.Settings;

namespace RaysLoader.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    string SettingsDirectory { get; }
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
}

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _settingsPath;

    public SettingsService(string? settingsDirectory = null)
    {
        SettingsDirectory = settingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaysLoader");
        _settingsPath = Path.Combine(SettingsDirectory, "settings.json");
    }

    public AppSettings Current { get; private set; } = new();
    public string SettingsDirectory { get; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            var imported = await TryImportLegacySettingsAsync(cancellationToken).ConfigureAwait(false);
            if (imported is not null) imported.HasCompletedOnboarding = true;
            Current = imported ?? new AppSettings();
            await SaveAsync(Current, cancellationToken).ConfigureAwait(false);
            return Current;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var hasOnboardingFlag = document.RootElement.EnumerateObject().Any(property =>
                property.Name.Equals(nameof(AppSettings.HasCompletedOnboarding), StringComparison.OrdinalIgnoreCase));
            Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            if (!hasOnboardingFlag) Current.HasCompletedOnboarding = true;
            Normalize(Current);
            return Current;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or FormatException)
        {
            TryBackupCorruptSettings();
            Current = new AppSettings();
            return Current;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        Directory.CreateDirectory(SettingsDirectory);
        var temporaryPath = _settingsPath + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, _settingsPath, overwrite: true);
        Current = settings;
    }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        Current = new AppSettings { HasCompletedOnboarding = Current.HasCompletedOnboarding };
        return SaveAsync(Current, cancellationToken);
    }

    private async Task<AppSettings?> TryImportLegacySettingsAsync(CancellationToken cancellationToken)
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var legacyPath = Path.Combine(roaming, "YtDlpGui", "settings.json");
        if (!File.Exists(legacyPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(legacyPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            var result = new AppSettings();
            if (root.TryGetProperty("save_paths", out var savePaths))
            {
                result.DefaultOutputDirectory = ReadString(savePaths, "video") ?? ReadString(savePaths, "fallback") ?? result.DefaultOutputDirectory;
            }

            if (root.TryGetProperty("preferences", out var preferences))
            {
                result.DefaultVideoQuality = ReadString(preferences, "last_video_quality") ?? result.DefaultVideoQuality;
                result.DefaultContainer = ReadString(preferences, "last_output_format") ?? result.DefaultContainer;
            }

            if (root.TryGetProperty("auth", out var auth))
            {
                result.CookiesFile = ReadString(auth, "cookies_file") ?? string.Empty;
            }

            if (root.TryGetProperty("window", out var window))
            {
                result.WindowWidth = ReadInt(window, "width") ?? result.WindowWidth;
                result.WindowHeight = ReadInt(window, "height") ?? result.WindowHeight;
            }

            return result;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void TryBackupCorruptSettings()
    {
        try
        {
            var backup = Path.Combine(SettingsDirectory, $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(_settingsPath, backup, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void Normalize(AppSettings settings)
    {
        string[] videoQualities = ["best", "2160p", "1440p", "1080p", "720p", "480p", "360p"];
        if (!Enum.IsDefined(settings.AccentColor)) settings.AccentColor = AppAccent.System;
        if (!Enum.IsDefined(settings.AuthenticationMode)) settings.AuthenticationMode = AuthenticationMode.None;
        if (!Enum.IsDefined(settings.YtDlpUpdateChannel)) settings.YtDlpUpdateChannel = YtDlpUpdateChannel.Nightly;
        settings.BrowserCookies = AuthenticationOptions.Browsers.Any(option =>
            option.Value.Equals(settings.BrowserCookies, StringComparison.OrdinalIgnoreCase))
            ? settings.BrowserCookies.ToLowerInvariant()
            : string.Empty;
        if (settings.AuthenticationMode is AuthenticationMode.Browser && settings.BrowserCookies.Length == 0)
            settings.BrowserCookies = AuthenticationOptions.Browsers[0].Value;
        settings.MaximumSimultaneousDownloads = Math.Clamp(settings.MaximumSimultaneousDownloads, 1, 8);
        settings.ConcurrentFragments = Math.Clamp(settings.ConcurrentFragments, 1, 32);
        settings.WindowWidth = Math.Clamp(settings.WindowWidth, 800, 7680);
        settings.WindowHeight = Math.Clamp(settings.WindowHeight, 600, 4320);
        settings.DefaultVideoQuality = videoQualities.FirstOrDefault(
            quality => quality.Equals(settings.DefaultVideoQuality, StringComparison.OrdinalIgnoreCase)) ?? "best";
        settings.DefaultOutputDirectory = AppPaths.NormalizeOutputDirectory(settings.DefaultOutputDirectory);
    }

    private static string? ReadString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;
    private static int? ReadInt(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;
}
