[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$ToolsDirectory,
    [switch]$SkipTools
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $repoRoot 'artifacts\publish\win-x64'

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

. (Join-Path $PSScriptRoot 'publish-common.ps1')
Invoke-StagedPublish -ProjectPath (Join-Path $repoRoot 'src\RaysLoader\RaysLoader.csproj') `
    -Destination $publishDirectory -PublishArguments @(
        '-c', $Configuration, '-p:Platform=x64', '--self-contained', 'true',
        '-p:PublicRelease=true', '-p:PublishSingleFile=false')

if (-not $SkipTools) {
    if ([string]::IsNullOrWhiteSpace($ToolsDirectory)) {
        $ToolsDirectory = Join-Path $env:LOCALAPPDATA 'RaysLoader\tools'
        $legacyTools = Join-Path $env:LOCALAPPDATA 'YtDlpGui\tools'
        if (-not (Test-Path -LiteralPath $ToolsDirectory) -and (Test-Path -LiteralPath $legacyTools)) {
            $ToolsDirectory = $legacyTools
        }
    }

    $requiredTools = @('yt-dlp.exe', 'ffmpeg.exe', 'ffprobe.exe')
    $missingTools = @($requiredTools | Where-Object { -not (Test-Path -LiteralPath (Join-Path $ToolsDirectory $_) -PathType Leaf) })
    if ($missingTools.Count -gt 0) {
        Write-Warning "Tools were not bundled. Missing from '$ToolsDirectory': $($missingTools -join ', '). Use -ToolsDirectory or -SkipTools."
    } else {
        $destination = Join-Path $publishDirectory 'tools'
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        foreach ($tool in $requiredTools) {
            $sourcePath = Join-Path $ToolsDirectory $tool
            $destinationPath = Join-Path $destination $tool
            $alreadyCurrent = (Test-Path -LiteralPath $destinationPath -PathType Leaf) -and
                ((Get-FileHash -Algorithm SHA256 -LiteralPath $sourcePath).Hash -eq
                 (Get-FileHash -Algorithm SHA256 -LiteralPath $destinationPath).Hash)
            if (-not $alreadyCurrent) {
                Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
            }
        }
        Write-Host "Bundled validated tool set from $ToolsDirectory"
    }
}

Write-Host "Published Ray's Loader to $publishDirectory"
