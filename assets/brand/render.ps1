[CmdletBinding()]
param(
    [string]$OutputDirectory
)

# Renders the README and social images from brand.html with headless Chrome or Edge.
# The shot-*.png files are window captures of the public build; recapture them when the UI changes.

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'assets' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

$browser = @(
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $browser) { throw 'Chrome or Edge is required to render the brand images.' }

$page = ([Uri](Join-Path $PSScriptRoot 'brand.html')).AbsoluteUri
$profileDirectory = Join-Path ([IO.Path]::GetTempPath()) "RaysLoaderBrand-$([guid]::NewGuid().ToString('N'))"

# View id, window size, output path relative to the output directory.
$views = @(
    @('hero', '1600,900', 'screenshots\ray-loader-hero.png'),
    @('card-queue', '1520,1199', 'screenshots\manage-downloads.png'),
    @('card-add', '1520,1199', 'screenshots\add-downloads.png'),
    @('card-settings', '1520,1199', 'screenshots\customize-settings.png'),
    @('social', '1280,640', 'social-preview.png')
)

try {
    foreach ($view in $views) {
        $out = Join-Path $OutputDirectory $view[2]
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $out) | Out-Null
        $arguments = @(
            '--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run',
            "--user-data-dir=`"$profileDirectory`"", '--force-device-scale-factor=1',
            "--window-size=$($view[1])", '--virtual-time-budget=3000',
            "--screenshot=`"$out`"", "$page`?v=$($view[0])"
        )
        $process = Start-Process -FilePath $browser -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $out)) { throw "Rendering $($view[0]) failed." }
        Write-Host "Rendered $out"
    }
}
finally {
    if (Test-Path -LiteralPath $profileDirectory) { Remove-Item -LiteralPath $profileDirectory -Recurse -Force -ErrorAction SilentlyContinue }
}
