[CmdletBinding()]
param(
    [string]$IsccPath,
    [string]$ToolsDirectory,
    [switch]$SkipPublish,
    [switch]$SkipTools
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish-windows.ps1') -ToolsDirectory $ToolsDirectory -SkipTools:$SkipTools
}

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($IsccPath) -or -not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw 'Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup or pass -IsccPath.'
}

& $IsccPath (Join-Path $repoRoot 'installer\setup.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }
Write-Host "Created $(Join-Path $repoRoot 'artifacts\RaysLoader-Setup-1.3.2.exe')"
