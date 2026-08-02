[CmdletBinding()]
param(
    [string]$ToolsDirectory,
    [switch]$SkipPublish,
    [switch]$SkipTools
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $repoRoot 'artifacts\publish\win-x64'
$archivePath = Join-Path $repoRoot 'artifacts\RaysLoader-Portable-win-x64.zip'

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish-windows.ps1') -ToolsDirectory $ToolsDirectory -SkipTools:$SkipTools
}
if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'RaysLoader.exe') -PathType Leaf)) {
    throw "Published executable not found at '$publishDirectory'. Run publish-windows.ps1 first."
}

if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal
Write-Host "Created $archivePath"
