[CmdletBinding()]
param(
    [string]$OutputDirectory
)

# Renders the "Bracket drop" app icon: corner brackets framing a down chevron
# that lands on a floor bar. Writes the public and DEV .png and .ico assets.
# Small sizes use a simplified, heavier drawing so they stay legible.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'src\RaysLoader\Assets' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

$tile = [System.Drawing.ColorTranslator]::FromHtml('#0B0B0B')
$ink = [System.Drawing.ColorTranslator]::FromHtml('#EDEDED')

function New-IconBitmap {
    param([int]$Size, [System.Drawing.Color]$Accent, [bool]$Dev)

    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear($tile)

    # Design units are a 256 grid.
    $u = $Size / 256.0
    $small = $Size -le 32
    $stroke = if ($small) { 26 } else { 16 }
    $inset = if ($small) { 22 } else { 30 }
    $arm = if ($small) { 70 } else { 58 }

    $inkBrush = New-Object System.Drawing.SolidBrush $ink
    $accentBrush = New-Object System.Drawing.SolidBrush $Accent

    # Rectangles snap to whole pixels so small sizes stay crisp.
    function Fill-Rect($brush, [double]$x, [double]$y, [double]$w, [double]$h) {
        $left = [Math]::Round($x * $u); $top = [Math]::Round($y * $u)
        $right = [Math]::Max($left + 1, [Math]::Round(($x + $w) * $u)); $bottom = [Math]::Max($top + 1, [Math]::Round(($y + $h) * $u))
        $g.FillRectangle($brush, [int]$left, [int]$top, [int]($right - $left), [int]($bottom - $top))
    }

    # Four corner brackets, drawn as filled L shapes so the joins stay square.
    $far = 256 - $inset
    $corners = @(
        @{ X = $inset; Y = $inset; DX = 1; DY = 1; Name = 'TL' },
        @{ X = $far; Y = $inset; DX = -1; DY = 1; Name = 'TR' },
        @{ X = $inset; Y = $far; DX = 1; DY = -1; Name = 'BL' },
        @{ X = $far; Y = $far; DX = -1; DY = -1; Name = 'BR' }
    )
    foreach ($c in $corners) {
        $brush = if ($Dev -and $c.Name -eq 'TR') { $accentBrush } else { $inkBrush }
        $hx = if ($c.DX -gt 0) { $c.X } else { $c.X - $arm }
        $hy = if ($c.DY -gt 0) { $c.Y } else { $c.Y - $stroke }
        Fill-Rect $brush $hx $hy $arm $stroke
        $vx = if ($c.DX -gt 0) { $c.X } else { $c.X - $stroke }
        $vy = if ($c.DY -gt 0) { $c.Y } else { $c.Y - $arm }
        Fill-Rect $brush $vx $vy $stroke $arm
    }

    if ($small) {
        # Chevron and floor only: a stem would blur into the chevron at 16-32px.
        $points = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF ([single][Math]::Round(62 * $u)), ([single][Math]::Round(82 * $u))),
            (New-Object System.Drawing.PointF ([single][Math]::Round(194 * $u)), ([single][Math]::Round(82 * $u))),
            (New-Object System.Drawing.PointF ([single][Math]::Round(128 * $u)), ([single][Math]::Round(160 * $u)))
        )
        $g.FillPolygon($accentBrush, $points)
        Fill-Rect $inkBrush 62 176 132 26
    }
    else {
        Fill-Rect $inkBrush 114 62 28 62
        $points = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF ([single][Math]::Round(72 * $u)), ([single][Math]::Round(116 * $u))),
            (New-Object System.Drawing.PointF ([single][Math]::Round(184 * $u)), ([single][Math]::Round(116 * $u))),
            (New-Object System.Drawing.PointF ([single][Math]::Round(128 * $u)), ([single][Math]::Round(172 * $u)))
        )
        $g.FillPolygon($accentBrush, $points)
        Fill-Rect $inkBrush 72 184 112 16
    }

    $inkBrush.Dispose(); $accentBrush.Dispose(); $g.Dispose()
    return $bitmap
}

function Get-PngBytes([System.Drawing.Bitmap]$Bitmap) {
    $stream = New-Object IO.MemoryStream
    $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray(); $stream.Dispose()
    return , $bytes
}

# A classic 32-bit DIB icon entry: BITMAPINFOHEADER, bottom-up BGRA pixels,
# then an all-zero AND mask (alpha carries transparency).
function Get-DibBytes([System.Drawing.Bitmap]$Bitmap) {
    $size = $Bitmap.Width
    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter $stream
    $writer.Write([uint32]40); $writer.Write([int32]$size); $writer.Write([int32]($size * 2))
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]0)
    $writer.Write([uint32]0); $writer.Write([int32]0); $writer.Write([int32]0)
    $writer.Write([uint32]0); $writer.Write([uint32]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $p = $Bitmap.GetPixel($x, $y)
            $writer.Write([byte]$p.B); $writer.Write([byte]$p.G); $writer.Write([byte]$p.R); $writer.Write([byte]$p.A)
        }
    }
    $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)
    $writer.Write((New-Object byte[] ($maskRow * $size)))
    $writer.Flush()
    $bytes = $stream.ToArray(); $writer.Dispose()
    return , $bytes
}

function Write-Icon {
    param([string]$Path, [System.Drawing.Color]$Accent, [bool]$Dev)

    $sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
    $images = foreach ($size in $sizes) {
        $bitmap = New-IconBitmap -Size $size -Accent $Accent -Dev $Dev
        if ($size -ge 256) { , (Get-PngBytes $bitmap) } else { , (Get-DibBytes $bitmap) }
        $bitmap.Dispose()
    }

    # ICO container: DIB entries for small sizes, PNG for 256 (the standard layout).
    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter $stream
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + (16 * $sizes.Count)
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write($image) }
    $writer.Flush()
    [IO.File]::WriteAllBytes($Path, $stream.ToArray())
    $writer.Dispose()
}

function Write-Logo {
    param([string]$Path, [System.Drawing.Color]$Accent, [bool]$Dev)
    $bitmap = New-IconBitmap -Size 512 -Accent $Accent -Dev $Dev
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

$publicAccent = [System.Drawing.ColorTranslator]::FromHtml('#B8FF1F')
$devAccent = [System.Drawing.ColorTranslator]::FromHtml('#FF2B2B')

Write-Logo (Join-Path $OutputDirectory 'RaysLoader.png') $publicAccent $false
Write-Icon (Join-Path $OutputDirectory 'RaysLoader.ico') $publicAccent $false
Write-Logo (Join-Path $OutputDirectory 'RaysLoader.Dev.png') $devAccent $true
Write-Icon (Join-Path $OutputDirectory 'RaysLoader.Dev.ico') $devAccent $true

Write-Host "Wrote Bracket drop icons to $OutputDirectory"
