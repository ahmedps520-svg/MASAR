<#
.SYNOPSIS
  Makes the MASAR app icon from the game logo (game/Assets/Masar/Art/Brand/MasarLogo.png,
  provided by the owner): resized to 1024x1024, opaque (App Store icons must have no alpha).
  Output: game/Assets/Masar/Art/Icon/AppIcon.png
#>
Add-Type -AssemblyName System.Drawing
$repo = Split-Path -Parent $PSScriptRoot
$src = Join-Path $repo "game\Assets\Masar\Art\Brand\MasarLogo.png"
$dir = Join-Path $repo "game\Assets\Masar\Art\Icon"
New-Item -ItemType Directory -Force $dir | Out-Null
$S = 1024
$logo = [System.Drawing.Image]::FromFile($src)
$bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = "HighQualityBicubic"; $g.PixelOffsetMode = "HighQuality"; $g.SmoothingMode = "AntiAlias"
$g.DrawImage($logo, 0, 0, $S, $S)
$out = Join-Path $dir "AppIcon.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $logo.Dispose()
Write-Host "Wrote $out"
