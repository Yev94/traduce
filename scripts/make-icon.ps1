# Converts the generated transparent PNG into standard Windows icon sizes.
param([string]$Source = (Join-Path $PSScriptRoot '..\assets\logo.png'), [string]$Output = (Join-Path $PSScriptRoot '..\assets\traduce.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$original = [Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Source))
$frames = @()
try {
    foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
        $bitmap = New-Object Drawing.Bitmap($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = New-Object IO.MemoryStream
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($original, 0, 0, $size, $size)
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $frames += [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
        } finally { $graphics.Dispose(); $bitmap.Dispose(); $stream.Dispose() }
    }
    $file = [IO.File]::Create([IO.Path]::GetFullPath($Output))
    $writer = New-Object IO.BinaryWriter($file)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    } finally { $writer.Dispose(); $file.Dispose() }
} finally { $original.Dispose() }
Write-Output $Output
