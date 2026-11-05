[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [string]$ToolsDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$devRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\dev'))
$publishDirectory = [IO.Path]::GetFullPath((Join-Path $devRoot 'win-x64'))
$expectedPrefix = $devRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

if (-not $publishDirectory.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to publish outside the managed DEV artifacts directory: $publishDirectory"
}

if (Get-Process RaysLoader -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $publishDirectory 'RaysLoader.exe') }) {
    throw 'Close Ray''s Loader DEV before publishing. The live application can remain open.'
}

if ($ToolsDirectory) {
    $ToolsDirectory = (Resolve-Path -LiteralPath $ToolsDirectory).Path
    if ($ToolsDirectory.StartsWith($publishDirectory, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Use a tools source outside the DEV publish directory.'
    }
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

dotnet publish (Join-Path $repoRoot 'src\RaysLoader\RaysLoader.csproj') `
    -c $Configuration -p:Platform=x64 --self-contained true `
    -p:PublicRelease=false -p:PublishSingleFile=false -o $publishDirectory

if ($LASTEXITCODE -ne 0) { throw "DEV publish failed (exit code $LASTEXITCODE)." }

if ($ToolsDirectory) {
    $toolDestination = Join-Path $publishDirectory 'tools'
    New-Item -ItemType Directory -Path $toolDestination -Force | Out-Null
    foreach ($toolName in @('yt-dlp.exe', 'ffmpeg.exe', 'ffprobe.exe', 'deno.exe')) {
        $toolSource = Join-Path $ToolsDirectory $toolName
        if (Test-Path -LiteralPath $toolSource -PathType Leaf) {
            Copy-Item -LiteralPath $toolSource -Destination (Join-Path $toolDestination $toolName)
        }
    }
}

Write-Host "Published Ray's Loader DEV to $publishDirectory"
Write-Host 'DEV settings, managed tools, updates, and logs use %LOCALAPPDATA%\RaysLoaderDev.'
