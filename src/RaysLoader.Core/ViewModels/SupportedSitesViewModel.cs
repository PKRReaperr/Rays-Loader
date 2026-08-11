using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RaysLoader.Models;
using RaysLoader.ProcessManagement;
using RaysLoader.Services;

namespace RaysLoader.ViewModels;

public sealed partial class SupportedSitesViewModel(ISupportedSitesService supportedSitesService) : ObservableObject, IDisposable
{
    [ObservableProperty] private IReadOnlyList<SupportedSite> filteredSites = [];
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string ytDlpVersion = "Unknown";
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private bool isLoading;
    private IReadOnlyList<SupportedSite> allSites = [];
    private CancellationTokenSource? loadCancellation;
    private bool hasLoaded;

    public string CountText => IsLoading
        ? "Reading the installed yt-dlp extractor list..."
        : allSites.Count == 0
            ? "No extractors loaded"
            : $"{FilteredSites.Count:N0} of {allSites.Count:N0} extractors";
    public string VersionText => $"Installed yt-dlp: {YtDlpVersion}";
    public bool HasNoResults => !IsLoading && ErrorMessage.Length == 0 && FilteredSites.Count == 0;

    public Task LoadAsync() => hasLoaded ? Task.CompletedTask : LoadCoreAsync(forceRefresh: false);

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => LoadCoreAsync(forceRefresh: true);

    public void CancelLoading() => loadCancellation?.Cancel();

    public void Dispose()
    {
        var cancellation = Interlocked.Exchange(ref loadCancellation, null);
        if (cancellation is null) return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasNoResults));
        RefreshCommand.NotifyCanExecuteChanged();
    }

    partial void OnYtDlpVersionChanged(string value) => OnPropertyChanged(nameof(VersionText));

    private bool CanRefresh() => !IsLoading;

    private async Task LoadCoreAsync(bool forceRefresh)
    {
        var cancellation = new CancellationTokenSource();
        if (Interlocked.CompareExchange(ref loadCancellation, cancellation, null) is not null)
        {
            cancellation.Dispose();
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            var catalog = await supportedSitesService.GetSupportedSitesAsync(forceRefresh, cancellation.Token);
            allSites = catalog.Sites;
            YtDlpVersion = catalog.YtDlpVersion;
            hasLoaded = true;
            ApplyFilter();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is MissingToolException or YtDlpException or ProcessLaunchException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            allSites = [];
            FilteredSites = [];
            ErrorMessage = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref loadCancellation, null, cancellation), cancellation))
            {
                cancellation.Dispose();
            }
            IsLoading = false;
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(HasNoResults));
        }
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        FilteredSites = query.Length == 0
            ? allSites
            : allSites.Where(site =>
                    site.ExtractorName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    site.SiteName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    site.Detail.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasNoResults));
    }
}
