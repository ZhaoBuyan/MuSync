# MuSync icon generator: crop -> enhance -> circle mask -> multi-size ICO + previews
# Usage: powershell -File tools/make-icon.ps1 [-SourcePath <png>]
# Without -SourcePath, uses the most recently modified png in Downloads.
param([string]$SourcePath = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return , $bytes
}

# ---- locate source png ----
if ($SourcePath) {
    $srcFile = Get-Item $SourcePath
} else {
    $dl = Join-Path $env:USERPROFILE 'Downloads'
    $cands = @(Get-ChildItem (Join-Path $dl '*.png') | Sort-Object LastWriteTime -Descending)
    Write-Output "png candidates in Downloads:"
    foreach ($c in $cands) { Write-Output ("  [" + $c.LastWriteTime.ToString('yyyy-MM-dd HH:mm') + "] " + $c.Name + "  " + $c.Length + " bytes") }
    if ($cands.Count -eq 0) { throw 'no png found in Downloads' }
    $srcFile = $cands[0]
}
$src = [System.Drawing.Image]::FromFile($srcFile.FullName)
Write-Output ("source: " + $srcFile.FullName)
Write-Output ("source size: " + $src.Width + "x" + $src.Height)

# ---- crop square focused on head + star + scarf ----
$side = [int]([Math]::Min($src.Width, $src.Height) * 0.64)
$cx = [int]($src.Width * 0.17)
$cy = [int]($src.Height * 0.03)
if (($cx + $side) -gt $src.Width)  { $cx = $src.Width - $side }
if (($cy + $side) -gt $src.Height) { $cy = $src.Height - $side }
Write-Output ("crop: x=$cx y=$cy side=$side")

$bmp = New-Object System.Drawing.Bitmap($side, $side, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.DrawImage($src,
    (New-Object System.Drawing.Rectangle(0, 0, $side, $side)),
    (New-Object System.Drawing.Rectangle($cx, $cy, $side, $side)),
    [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$src.Dispose()

# ---- line-art contrast enhance: darks darker, paper pure white ----
$rect = New-Object System.Drawing.Rectangle(0, 0, $side, $side)
$data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadWrite, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bytes = New-Object byte[] ($data.Stride * $side)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
for ($i = 0; $i -lt $bytes.Length; $i += 4) {
    $b = $bytes[$i]; $gg = $bytes[$i + 1]; $r = $bytes[$i + 2]
    $lum = 0.299 * $r + 0.587 * $gg + 0.114 * $b
    $v = [int](255 * [Math]::Pow($lum / 255.0, 1.35))
    if ($v -gt 238) { $v = 255 }
    if ($v -lt 28)  { $v = 0 }
    $bytes[$i] = [byte]$v; $bytes[$i + 1] = [byte]$v; $bytes[$i + 2] = [byte]$v
    $bytes[$i + 3] = 255
}
[System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $data.Scan0, $bytes.Length)
$bmp.UnlockBits($data)
$bmp.Save((Join-Path (Get-Location) 'icon-cropped.png'), [System.Drawing.Imaging.ImageFormat]::Png)

# ---- render sizes with circular alpha mask ----
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$blobs = @()
foreach ($s in $sizes) {
    $tb = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $tg = [System.Drawing.Graphics]::FromImage($tb)
    $tg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $tg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $tg.DrawImage($bmp, (New-Object System.Drawing.Rectangle(0, 0, $s, $s)))
    $tg.Dispose()
    $r2 = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $d2 = $tb.LockBits($r2, [System.Drawing.Imaging.ImageLockMode]::ReadWrite, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bb = New-Object byte[] ($d2.Stride * $s)
    [System.Runtime.InteropServices.Marshal]::Copy($d2.Scan0, $bb, 0, $bb.Length)
    $cen = ($s - 1) / 2.0
    $rad = $s / 2.0
    for ($y = 0; $y -lt $s; $y++) {
        for ($x = 0; $x -lt $s; $x++) {
            $dx = $x - $cen; $dy = $y - $cen
            $dist = [Math]::Sqrt($dx * $dx + $dy * $dy)
            $cov = $rad - $dist + 0.5
            if ($cov -le 0) { $cov = 0.0 } elseif ($cov -ge 1) { $cov = 1.0 }
            $idx = $y * $d2.Stride + $x * 4 + 3
            $bb[$idx] = [byte][int]($bb[$idx] * $cov)
        }
    }
    [System.Runtime.InteropServices.Marshal]::Copy($bb, 0, $d2.Scan0, $bb.Length)
    $tb.UnlockBits($d2)
    $blobs += , (Get-PngBytes $tb)
    $tb.Dispose()
}
Write-Output ("rendered sizes: " + ($sizes -join ','))

# ---- write ICO (PNG-compressed entries, Vista+) ----
$icoPath = Join-Path (Get-Location) 'icon-new.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $blob = $blobs[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$blob.Length); $bw.Write([UInt32]$offset)
    $offset += $blob.Length
}
foreach ($blob in $blobs) { $bw.Write($blob) }
$bw.Dispose(); $fs.Dispose()
Write-Output ("ico written: " + $icoPath + "  " + (Get-Item $icoPath).Length + " bytes")

# ---- preview sheets ----
$pad = 12
$previewSizes = @(256, 128, 64, 48, 32, 24, 16)
$sumW = 0; foreach ($s in $previewSizes) { $sumW += $s }
$rowH = 256
$totalW = $pad + $sumW + $pad * $previewSizes.Count
$sheetH = $rowH * 2 + $pad * 2
$sheet = [System.Drawing.Bitmap]::new($totalW, $sheetH)
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.Clear([System.Drawing.Color]::White)
$darkBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(45, 45, 48))
$sg.FillRectangle($darkBrush, 0, $rowH + $pad, $totalW, $rowH + $pad)
$x = $pad
foreach ($s in $previewSizes) {
    $msTmp = New-Object System.IO.MemoryStream(, $blobs[$sizes.IndexOf($s)])
    $img = [System.Drawing.Image]::FromStream($msTmp)
    $y = $pad + [int](($rowH - $s) / 2)
    $sg.DrawImage($img, $x, $y, $s, $s)
    $sg.DrawImage($img, $x, $rowH + $pad + $y, $s, $s)
    $x += $s + $pad
    $img.Dispose()
    $msTmp.Dispose()
}
$sg.Dispose()
$sheet.Save((Join-Path (Get-Location) 'icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)

# zoom sheet for small sizes (bicubic x6)
$zoomSizes = @(16, 24, 32, 48)
$scale = 6
$zw = $pad
foreach ($s in $zoomSizes) { $zw += $s * $scale + $pad }
$zh = 48 * $scale + $pad * 2
$zoom = [System.Drawing.Bitmap]::new($zw, $zh)
$zg = [System.Drawing.Graphics]::FromImage($zoom)
$zg.Clear([System.Drawing.Color]::FromArgb(230, 230, 230))
$zg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$zx = $pad
foreach ($s in $zoomSizes) {
    $msTmp = New-Object System.IO.MemoryStream(, $blobs[$sizes.IndexOf($s)])
    $img = [System.Drawing.Image]::FromStream($msTmp)
    $zg.DrawImage($img, $zx, $pad + (48 - $s) * $scale / 2, $s * $scale, $s * $scale)
    $zx += $s * $scale + $pad
    $img.Dispose()
    $msTmp.Dispose()
}
$zg.Dispose()
$zoom.Save((Join-Path (Get-Location) 'icon-zoom.png'), [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output 'previews written: icon-preview.png / icon-zoom.png'
Write-Output 'done'
