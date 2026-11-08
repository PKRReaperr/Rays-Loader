[CmdletBinding()]
param(
    [string]$OutputDirectory
)

# Renders the README, social and installer wizard images from brand.html with headless Chrome or Edge.
# The shot-*.png files are window captures of the public build; recapture them when the UI changes.
# Output paths are relative to -OutputDirectory, which defaults to the repository root.

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $OutputDirectory) { $OutputDirectory = $repoRoot }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

$browser = @(
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $browser) { throw 'Chrome or Edge is required to render the brand images.' }

$page = ([Uri](Join-Path $PSScriptRoot 'brand.html')).AbsoluteUri
$profileDirectory = Join-Path ([IO.Path]::GetTempPath()) "RaysLoaderBrand-$([guid]::NewGuid().ToString('N'))"

# View id, window size, output path relative to the output directory, device scale factor.
# Inno Setup picks the wizard image that best fits the display DPI, so those render at several scales.
$views = @(
    @('hero', '1600,900', 'assets\screenshots\ray-loader-hero.png', 1),
    @('card-queue', '1520,1199', 'assets\screenshots\manage-downloads.png', 1),
    @('card-add', '1520,1199', 'assets\screenshots\add-downloads.png', 1),
    @('card-settings', '1520,1199', 'assets\screenshots\customize-settings.png', 1),
    @('social', '1280,640', 'assets\social-preview.png', 1)
)
foreach ($scale in 100, 150, 200) {
    $views += , @('wizard', '164,314', "installer\images\WizardImage-$scale.png", ($scale / 100))
    $views += , @('wizard-small', '58,58', "installer\images\WizardSmallImage-$scale.png", ($scale / 100))
}

try {
    foreach ($view in $views) {
        $out = Join-Path $OutputDirectory $view[2]
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $out) | Out-Null
        $arguments = @(
            '--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run',
            "--user-data-dir=`"$profileDirectory`"", "--force-device-scale-factor=$($view[3])",
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
