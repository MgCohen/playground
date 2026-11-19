# Usage: strip.ps1 <gif> <times file> <out.jpg> [nth]: five frames around the nth empty frame (frame mean = the mean of the first frame), side by side.
param([string]$Gif, [string]$Times, [string]$Out, [int]$Nth = 1)
Add-Type -AssemblyName System.Drawing
$rows = Get-Content $Times | Where-Object { $_.Trim() } | ForEach-Object { , ($_ -split ' ') }
$empty = [double]$rows[0][1]
$found = 0; $idx = -1
for ($i = 1; $i -lt $rows.Count; $i++) {
  $v = [double]$rows[$i][1]; $p = [double]$rows[$i - 1][1]
  if ([math]::Abs($v - $empty) -lt 0.6 -and [math]::Abs($p - $empty) -gt 2) { $found++; if ($found -eq $Nth) { $idx = $i; break } }
}
if ($idx -lt 0) { "no empty frame"; exit 1 }
$img = [System.Drawing.Image]::FromFile($Gif)
$dim = New-Object System.Drawing.Imaging.FrameDimension($img.FrameDimensionsList[0])
$h = 320; $tiles = @()
foreach ($i in ($idx - 2)..($idx + 2)) {
  [void]$img.SelectActiveFrame($dim, $i)
  $w = [int]($img.Width * $h / $img.Height)
  $t = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($t)
  $g.InterpolationMode = 'HighQualityBicubic'; $g.DrawImage($img, 0, 0, $w, $h)
  $g.FillRectangle([System.Drawing.Brushes]::Black, 0, 0, $w, 20)
  $label = "$($rows[$i][0])  mean $($rows[$i][1])"; if ($i -eq $idx) { $label += "  EMPTY" }
  $brush = if ($i -eq $idx) { [System.Drawing.Brushes]::OrangeRed } else { [System.Drawing.Brushes]::White }
  $g.DrawString($label, (New-Object System.Drawing.Font 'Segoe UI', 9), $brush, 3, 2); $g.Dispose(); $tiles += $t
}
$W = 0; foreach ($t in $tiles) { $W += $t.Width + 4 }
$s = New-Object System.Drawing.Bitmap $W, $h
$g = [System.Drawing.Graphics]::FromImage($s); $g.Clear([System.Drawing.Color]::FromArgb(40, 40, 40)); $x = 0
foreach ($t in $tiles) { $g.DrawImage($t, $x, 0); $x += $t.Width + 4 }
$enc = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
$ep = New-Object System.Drawing.Imaging.EncoderParameters 1
$ep.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter([System.Drawing.Imaging.Encoder]::Quality, [long]70)
$s.Save($Out, $enc, $ep); "frame $idx, $($s.Width) x $h"
