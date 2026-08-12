[CmdletBinding()]
param(
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$devRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\dev'))
$publishDirectory = [IO.Path]::GetFullPath((Join-Path $devRoot 'win-x64'))
$expectedPrefix = $devRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

if (-not $publishDirectory.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to publish outside the managed DEV artifacts directory: $publishDirectory"
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

dotnet publish (Join-Path $repoRoot 'src\RaysLoader\RaysLoader.csproj') `
    -c $Configuration -p:Platform=x64 --self-contained true `
    -p:PublicRelease=false -p:PublishSingleFile=false -o $publishDirectory

Write-Host "Published Ray's Loader DEV to $publishDirectory"
