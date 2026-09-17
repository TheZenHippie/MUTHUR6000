Add-Type -AssemblyName System.Drawing
$pngPath = Join-Path $PSScriptRoot "icon.png"
$icoPath = Join-Path $PSScriptRoot "icon.ico"

if (-not (Test-Path $pngPath)) {
    Write-Error "icon.png not found at $pngPath"
    exit 1
}

$img = [System.Drawing.Image]::FromFile($pngPath)
$sizes = @(256, 128, 64, 48, 32, 16)
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

# Header: reserved (0), type (1 for ico), count
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)

$offset = 6 + ($sizes.Count * 16)
$bmpStreams = @()

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($img, 0, 0, $size, $size)
    $g.Dispose()

    $bmpStream = New-Object System.IO.MemoryStream
    $bmp.Save($bmpStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $bmpStreams += $bmpStream

    $bw.Write([byte]($size -band 255))
    $bw.Write([byte]($size -band 255))
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1) # color planes
    $bw.Write([uint16]32) # bpp
    $bw.Write([uint32]$bmpStream.Length)
    $bw.Write([uint32]$offset)

    $offset += $bmpStream.Length
}

foreach ($s in $bmpStreams) {
    $bytes = $s.ToArray()
    $bw.Write($bytes)
    $s.Dispose()
}

$img.Dispose()
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose()
$ms.Dispose()

Write-Host "Generated icon.ico successfully!" -ForegroundColor Green

