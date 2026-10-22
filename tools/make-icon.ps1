# Regenerates src/Quire.App/Assets/Quire.ico: ./tools/make-icon.ps1 -Out src/Quire.App/Assets/Quire.ico
param([string]$Out, [string]$PreviewPng)
Add-Type -AssemblyName System.Drawing
# Quire icon: a flat blue square holding three white "cells", the first with an accent bar.
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($size in $sizes) {
    $bmp = New-Object Drawing.Bitmap $size, $size
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'None'; $g.PixelOffsetMode = 'Half'
    $g.Clear([Drawing.Color]::Transparent)
    $blue = [Drawing.Color]::FromArgb(255, 0, 103, 192)
    $g.FillRectangle((New-Object Drawing.SolidBrush $blue), 0, 0, $size, $size)
    $pad = [Math]::Max(2, [Math]::Round($size * 0.17))
    $gap = [Math]::Max(1, [Math]::Round($size * 0.07))
    $inner = $size - 2 * $pad
    $cellH = [Math]::Floor(($inner - 2 * $gap) / 3)
    $white = New-Object Drawing.SolidBrush ([Drawing.Color]::White)
    $accent = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 76, 194, 255))
    for ($i = 0; $i -lt 3; $i++) {
        $y = $pad + $i * ($cellH + $gap)
        $w = if ($i -eq 2) { [Math]::Round($inner * 0.7) } else { $inner }
        $g.FillRectangle($white, $pad, $y, $w, $cellH)
        if ($i -eq 0 -and $size -ge 24) {
            $g.FillRectangle($accent, $pad, $y, [Math]::Max(2, [Math]::Round($size * 0.06)), $cellH)
        }
    }
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    if ($size -eq 256 -and $PreviewPng) { $bmp.Save($PreviewPng, [Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    $pngs += , @($size, $ms.ToArray())
}
# ICO container with PNG-compressed entries (supported since Windows Vista).
$fs = [IO.File]::Create($Out); $bw = New-Object IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $s = $p[0]; $data = $p[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($p in $pngs) { $bw.Write($p[1]) }
$bw.Close()
"icon written: $Out"
