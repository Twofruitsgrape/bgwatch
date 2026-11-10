param([string]$OutDir = "E:\Projects\bgwatch\installer")

Add-Type -AssemblyName System.Drawing

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

function New-RoundedPath([int]$size, [single]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng([int]$size, [string]$path) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    # 深色圆角底
    $bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 32, 38, 56))
    $shape = New-RoundedPath $size ([single]($size * 0.22))
    $g.FillPath($bg, $shape)

    # 中间一条"扫描线"，暗示在盯着后台看
    $lineHeight = [int]($size * 0.055)
    $lineY = [int]($size * 0.66)
    $lineBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 92, 200, 150))
    $g.FillRectangle($lineBrush, [int]($size * 0.22), $lineY, [int]($size * 0.56), $lineHeight)

    # 三个不同大小的点，代表不同层级的占用
    $dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $r1 = [int]($size * 0.09)
    $g.FillEllipse($dotBrush, [int]($size * 0.24), [int]($size * 0.30), $r1, $r1)
    $r2 = [int]($size * 0.07)
    $g.FillEllipse($dotBrush, [int]($size * 0.45), [int]($size * 0.22), $r2, $r2)
    $r3 = [int]($size * 0.05)
    $g.FillEllipse($dotBrush, [int]($size * 0.66), [int]($size * 0.26), $r3, $r3)

    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $p = Join-Path $OutDir "icon-$s.png"
    New-IconPng $s $p
    $pngs += $p
    Write-Host ("  生成 {0}x{0}" -f $s)
}

# 组装成 .ico（Vista 起支持 PNG 压缩的条目）
$icoPath = Join-Path $OutDir "app.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)

$bw.Write([UInt16]0)                 # reserved
$bw.Write([UInt16]1)                 # type = icon
$bw.Write([UInt16]$sizes.Count)      # count

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $data = [System.IO.File]::ReadAllBytes($pngs[$i])
    $s = $sizes[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))   # width
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))   # height
    $bw.Write([byte]0)               # palette
    $bw.Write([byte]0)               # reserved
    $bw.Write([UInt16]1)             # planes
    $bw.Write([UInt16]32)            # bpp
    $bw.Write([UInt32]$data.Length)  # size
    $bw.Write([UInt32]$offset)       # offset
    $offset += $data.Length
}
foreach ($p in $pngs) {
    $bw.Write([System.IO.File]::ReadAllBytes($p))
}
$bw.Close()
$fs.Close()

foreach ($p in $pngs) { Remove-Item $p -Force }

$len = (Get-Item $icoPath).Length
Write-Host ""
Write-Host ("app.ico 生成完毕：{0} 字节，{1} 个尺寸" -f $len, $sizes.Count) -ForegroundColor Green
