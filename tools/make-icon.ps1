<#
.SYNOPSIS
  Draws the MASAR app icon (self-made, project-owned): asphalt, a perspective lane line,
  the black/yellow Jeddah kerb-stripe motif and the name. 1024x1024, opaque (App Store
  icons must have no alpha). Output: game/Assets/Masar/Art/Icon/AppIcon.png
#>
Add-Type -AssemblyName System.Drawing
$repo = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $repo "game\Assets\Masar\Art\Icon"
New-Item -ItemType Directory -Force $dir | Out-Null
$S = 1024
$bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = "AntiAlias"; $g.TextRenderingHint = "AntiAliasGridFit"; $g.InterpolationMode = "HighQualityBicubic"

# Sky-to-asphalt background
$rect = New-Object System.Drawing.Rectangle(0, 0, $S, $S)
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255, 24, 30, 38), [System.Drawing.Color]::FromArgb(255, 10, 12, 15), 90)
$g.FillRectangle($bg, $rect)

# Road in perspective: a trapezoid from the bottom edge to a vanishing point
$road = New-Object System.Drawing.Drawing2D.GraphicsPath
$road.AddPolygon(@((New-Object System.Drawing.PointF(80, $S)), (New-Object System.Drawing.PointF(944, $S)), (New-Object System.Drawing.PointF(560, 430)), (New-Object System.Drawing.PointF(464, 430))))
$roadBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.Rectangle(0, 430, $S, 594)), [System.Drawing.Color]::FromArgb(255, 40, 44, 50), [System.Drawing.Color]::FromArgb(255, 58, 62, 68), 90)
$g.FillPath($roadBrush, $road)

# Dashed center line (dashes shrink toward the horizon)
$white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 236, 236, 230))
$y = $S; $len = 150.0
while ($y -gt 450) {
  $t0 = ($y - 430) / 594.0; $y1 = $y - $len; if ($y1 -lt 430) { $y1 = 430 }
  $t1 = ($y1 - 430) / 594.0
  $w0 = 6 + 22 * $t0; $w1 = 6 + 22 * $t1
  $g.FillPolygon($white, @((New-Object System.Drawing.PointF((512 - $w0), $y)), (New-Object System.Drawing.PointF((512 + $w0), $y)), (New-Object System.Drawing.PointF((512 + $w1), $y1)), (New-Object System.Drawing.PointF((512 - $w1), $y1))))
  $y = $y1 - $len * 0.7; $len = $len * 0.62
}

# Kerb stripes along both road edges
$yellow = [System.Drawing.Color]::FromArgb(255, 227, 168, 18); $black = [System.Drawing.Color]::FromArgb(255, 16, 16, 18)
foreach ($side in -1, 1) {
  $n = 9
  for ($i = 0; $i -lt $n; $i++) {
    $ta = [Math]::Pow(($i) / $n, 1.6); $tb = [Math]::Pow(($i + 1) / $n, 1.6)
    $ya = $S - $ta * 594; $yb = $S - $tb * 594
    $xa = 512 + $side * (432 - 384 * $ta); $xb = 512 + $side * (432 - 384 * $tb)
    $wa = 46 * (1 - $ta * 0.85); $wb = 46 * (1 - $tb * 0.85)
    $brush = New-Object System.Drawing.SolidBrush($(if ($i % 2 -eq 0) { $yellow } else { $black }))
    $g.FillPolygon($brush, @((New-Object System.Drawing.PointF($xa, $ya)), (New-Object System.Drawing.PointF(($xa + $side * $wa), $ya)), (New-Object System.Drawing.PointF(($xb + $side * $wb), $yb)), (New-Object System.Drawing.PointF($xb, $yb))))
  }
}

# Name
$font = New-Object System.Drawing.Font("Segoe UI Black", 176, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = "Center"
$shadow = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(160, 0, 0, 0))
$g.DrawString("MASAR", $font, $shadow, (New-Object System.Drawing.RectangleF(6, 150, $S, 260)), $fmt)
$g.DrawString("MASAR", $font, $white, (New-Object System.Drawing.RectangleF(0, 142, $S, 260)), $fmt)
$accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0, 158, 107))
$g.FillRectangle($accent, 352, 372, 320, 14)

$out = Join-Path $dir "AppIcon.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "Icon written: $out"
