using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using RaysLoader.ProcessManagement;
using RaysLoader.Services;
using RaysLoader.ViewModels;
using RaysLoader.Views;

namespace RaysLoader;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            WriteCrashLog(args.Exception);
            args.Handled = false;
        };
    }

    public static IServiceProvider Services { get; private set; } = null!;
    public static MainWindow MainWindow { get; private set; } = null!;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var settings = new SettingsService();
            await settings.LoadAsync();

            var services = new ServiceCollection();
            services.AddSingleton<ISettingsService>(settings);
            services.AddSingleton<IUiDispatcher>(new DispatcherQueueService(DispatcherQueue.GetForCurrentThread()));
            services.AddSingleton<IProcessRunnerService, ProcessRunnerService>();
            services.AddSingleton<IYtDlpArgumentBuilder, YtDlpArgumentBuilder>();
            services.AddSingleton<IProgressParserService, ProgressParserService>();
            services.AddSingleton<IToolLocatorService, ToolLocatorService>();
            services.AddSingleton<IToolMaintenanceService, ToolMaintenanceService>();
            services.AddSingleton<IYtDlpService, YtDlpService>();
            services.AddSingleton<ISupportedSitesService, SupportedSitesService>();
            services.AddSingleton<IUpdateService>(new GitHubUpdateService(
                new HttpClient(),
                Path.Combine(settings.SettingsDirectory, "updates")));
            services.AddSingleton<IAppDataCleanupService, AppDataCleanupService>();
            services.AddSingleton<IDownloadExecutor, DownloadExecutor>();
            services.AddSingleton<IDownloadQueueService, DownloadQueueService>();
            services.AddSingleton<MainViewModel>();
            services.AddTransient<AddDownloadViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<SupportedSitesViewModel>();
            services.AddTransient<DownloadsPage>();
            services.AddTransient<SettingsPage>();
            services.AddTransient<AboutPage>();
            services.AddTransient<SupportedSitesPage>();
            services.AddSingleton<MainWindow>();

            Services = services.BuildServiceProvider();
            MainWindow = Services.GetRequiredService<MainWindow>();
            MainWindow.Activate();
        }
        catch (Exception exception)
        {
            WriteCrashLog(exception);
            throw;
        }
    }

    private static void WriteCrashLog(Exception exception)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RaysLoader");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "crash.log"), $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}");
        }
        catch (Exception loggingException) when (loggingException is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(loggingException);
        }
    }
}
