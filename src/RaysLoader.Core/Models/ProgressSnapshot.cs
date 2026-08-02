namespace RaysLoader.Models;

public sealed record ProgressSnapshot(
    double? Percent = null,
    string Speed = "",
    string Eta = "",
    long? DownloadedBytes = null,
    long? TotalBytes = null,
    int? FragmentIndex = null,
    int? FragmentCount = null,
    int? PlaylistIndex = null,
    int? PlaylistCount = null,
    string Operation = "");
