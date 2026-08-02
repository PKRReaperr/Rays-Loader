<p align="center">
  <img src="assets/screenshots/ray-loader-hero.png" alt="Ray's Loader" width="100%">
</p>

# Ray's Loader

Ray's Loader is a modern Windows 11 media downloader powered by yt-dlp.

## Download

[Download the latest Windows installer](https://github.com/PKRReaperr/Rays-Loader/releases/latest/download/RaysLoader-Setup-1.3.0.exe)

Run the downloaded `.exe` and follow the installer. Windows may show a SmartScreen warning because the installer is not currently code-signed.

The primary application is a native C#/.NET 8 WinUI 3 desktop app. It provides a Fluent, resizable download queue; asynchronous media analysis; video, audio, playlist, subtitle, metadata, thumbnail, and clip downloads; per-item progress and live terminal logs; cancellation and retry; persistent settings; verified in-app updates; and selectable bundled or external yt-dlp/FFmpeg tools. Ray's Loader automatically enables a bundled Deno runtime or a compatible Node installation for modern YouTube extraction.

## Features

### Manage downloads

Track queued, running, completed, and failed items in one clean view.

<p align="center">
  <img src="assets/screenshots/manage-downloads.png" alt="Manage downloads in Ray's Loader" width="760">
</p>

### Add downloads instantly

Paste links, choose formats, set quality, and send everything straight to the queue.

<p align="center">
  <img src="assets/screenshots/add-downloads.png" alt="Add a download in Ray's Loader" width="760">
</p>

### Customize your setup

Choose your theme, accent color, output folder, and download preferences.

<p align="center">
  <img src="assets/screenshots/customize-settings.png" alt="Customize Ray's Loader settings" width="760">
</p>

## Quick start

```powershell
dotnet restore RaysLoader.sln -p:Platform=x64
dotnet run --project src\RaysLoader\RaysLoader.csproj -c Debug -p:Platform=x64
```

For release builds, the installer, portable archive, tool layout, and troubleshooting, see [BUILDING.md](BUILDING.md).

Settings are stored in `%LOCALAPPDATA%\RaysLoader`. Portable builds deliberately use the same per-user location so they also work from read-only folders.

## Feedback and support

- [Report a bug](https://github.com/PKRReaperr/Rays-Loader/issues/new?template=1-bug-report.yml)
- [Suggest a feature](https://github.com/PKRReaperr/Rays-Loader/issues/new?template=2-feature-request.yml)
- [Browse existing feedback](https://github.com/PKRReaperr/Rays-Loader/issues)

Please search existing issues before submitting feedback. Remove passwords, browser cookies, authentication tokens, private URLs, and other sensitive information from reports and logs. Security vulnerabilities must be reported privately according to the [security policy](SECURITY.md).

Ray's Loader is not affiliated with YouTube or any supported media website. Users are responsible for complying with applicable laws, website terms, and content rights.

## License

Ray's Loader is released under the [MIT License](LICENSE). Dependency licensing and attribution are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
