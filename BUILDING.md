# Building Ray's Loader locally

## Requirements

- Windows 10 1809 or newer; Windows 11 is recommended.
- .NET 8 SDK.
- Inno Setup 6 only when creating `RaysLoader-Setup-1.2.0.exe`.
- Optional distributable `yt-dlp.exe`, `ffmpeg.exe`, and `ffprobe.exe` in `%LOCALAPPDATA%\RaysLoader\tools` or supplied with `-ToolsDirectory`.
- For complete YouTube extraction, place Deno 2.3+ as `deno.exe` beside the bundled tools or install Node 22+. Ray's Loader detects Deno first and then Node and passes its full path to yt-dlp.

All commands below run from the repository root in PowerShell. No remote repository, Docker, or CI service is required.

## Restore, build, run, and test

```powershell
dotnet restore RaysLoader.sln -p:Platform=x64
dotnet build RaysLoader.sln -c Debug -p:Platform=x64 --no-restore
dotnet run --project src\RaysLoader\RaysLoader.csproj -c Debug -p:Platform=x64 --no-restore
dotnet build RaysLoader.sln -c Release -p:Platform=x64 --no-restore
dotnet test tests\RaysLoader.Tests\RaysLoader.Tests.csproj -c Release -p:Platform=x64 --no-restore
```

## Publish self-contained x64

```powershell
.\scripts\publish-windows.ps1
```

The app is published to `artifacts\publish\win-x64`. The script stages tools when all three executables are present. Use `-SkipTools` for an app-only build, or:

```powershell
.\scripts\publish-windows.ps1 -ToolsDirectory 'C:\path\to\verified-tools'
```

Only redistribute tool binaries obtained from official sources and comply with their licenses. The release scripts do not download executables.

## Build installer

Install Inno Setup 6, then run:

```powershell
.\scripts\build-installer.ps1
```

Override the compiler location with `-IsccPath` if needed. Output: `artifacts\RaysLoader-Setup-1.2.0.exe`.

## Build portable ZIP

```powershell
.\scripts\build-portable.ps1
```

Output: `artifacts\RaysLoader-Portable-win-x64.zip`.

## Troubleshooting

- If tools are not bundled, choose their paths on Settings > Tools. The app also checks `%LOCALAPPDATA%\RaysLoader\tools`, the legacy `%LOCALAPPDATA%\YtDlpGui\tools`, and `PATH`.
- Clip downloads and post-processing require FFmpeg.
- Delete or rename a corrupt `%LOCALAPPDATA%\RaysLoader\settings.json`; the app also backs up malformed settings automatically.
