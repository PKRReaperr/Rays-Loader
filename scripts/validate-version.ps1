[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ($ExpectedVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw "Project version '$ExpectedVersion' is not a stable major.minor.patch version."
}

[xml]$properties = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$projectVersion = @($properties.Project.PropertyGroup.Version | Where-Object { $_ })[0]
$assemblyVersion = @($properties.Project.PropertyGroup.AssemblyVersion | Where-Object { $_ })[0]
$fileVersion = @($properties.Project.PropertyGroup.FileVersion | Where-Object { $_ })[0]

$checks = @(
    @{ Name = 'Directory.Build.props Version'; Actual = [string]$projectVersion; Expected = $ExpectedVersion },
    @{ Name = 'Directory.Build.props AssemblyVersion'; Actual = [string]$assemblyVersion; Expected = "$ExpectedVersion.0" },
    @{ Name = 'Directory.Build.props FileVersion'; Actual = [string]$fileVersion; Expected = "$ExpectedVersion.0" }
)

foreach ($check in $checks) {
    if ($check.Actual -cne $check.Expected) {
        throw "$($check.Name) is '$($check.Actual)' but expected '$($check.Expected)'."
    }
}

$manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'src\RaysLoader\app.manifest') -Raw
if ($manifest -notmatch ('assemblyIdentity version="' + [regex]::Escape("$ExpectedVersion.0") + '"')) {
    throw "The application manifest version does not match $ExpectedVersion."
}

$installer = Get-Content -LiteralPath (Join-Path $repoRoot 'installer\setup.iss') -Raw
if ($installer -notmatch ('#define MyAppVersion "' + [regex]::Escape($ExpectedVersion) + '"')) {
    throw "The installer version does not match $ExpectedVersion."
}
if ($installer -notmatch 'OutputBaseFilename=RaysLoader-Setup-\{#MyAppVersion\}') {
    throw 'The installer output filename is not derived from MyAppVersion.'
}

$readme = Get-Content -LiteralPath (Join-Path $repoRoot 'README.md') -Raw
$expectedInstaller = "RaysLoader-Setup-$ExpectedVersion.exe"
if ($readme -notmatch [regex]::Escape($expectedInstaller)) {
    throw "The README download filename does not match $expectedInstaller."
}

Write-Host "Validated Ray's Loader release version $ExpectedVersion and asset $expectedInstaller."
