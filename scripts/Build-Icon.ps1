# Converts the user-provided floe.png without drawing or changing the logo.
param([string] $Source = (Join-Path (Split-Path -Parent $PSScriptRoot) 'floe.png'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/Floe.Desktop/Assets'
New-Item -ItemType Directory -Force -Path $assetDirectory | Out-Null
$original = [Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Source))
$frames = @()
try {
    foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $ratio = [Math]::Min($size / $original.Width, $size / $original.Height)
            $width = [int][Math]::Round($original.Width * $ratio)
            $height = [int][Math]::Round($original.Height * $ratio)
            $graphics.DrawImage($original, [int](($size-$width)/2), [int](($size-$height)/2), $width, $height)
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $frames += [pscustomobject]@{ Size=$size; Bytes=$stream.ToArray() }
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $output = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($output)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
        [IO.File]::WriteAllBytes((Join-Path $assetDirectory 'Floe.ico'), $output.ToArray())
    } finally { $writer.Dispose(); $output.Dispose() }
    Copy-Item -LiteralPath $Source -Destination (Join-Path $assetDirectory 'Floe.png') -Force
    Write-Output ('Converted original logo: {0}x{1}; ICO frames 16-256 px; PNG master retained.' -f $original.Width,$original.Height)
} finally { $original.Dispose() }
