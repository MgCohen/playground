# Usage: at.ps1 <gif> <times file> <out.jpg> <HH:mm:ss.fff,...>: the frames nearest to the given times, side by side.
param([string]$Gif, [string]$Times, [string]$Out, [string]$At)
Add-Type -AssemblyName System.Drawing
$rows = Get-Content $Times | Where-Object { $_.Trim() } | ForEach-Object { , ($_ -split ' ') }
$ts = $rows | ForEach-Object { [datetime]::ParseExact($_[0], 'HH:mm:ss.fff', $null) }
$img = [System.Drawing.Image]::FromFile($Gif)
$dim = New-Object System.Drawing.Imaging.FrameDimension($img.FrameDimensionsList[0])
$h = 420; $tiles = @()
foreach ($a in $At -split ',') {
  $t0 = [datetime]::ParseExact($a, 'HH:mm:ss.fff', $null); $best = 0; $bd = 1e9
  for ($i = 0; $i -lt $ts.Count; $i++) { $d = [math]::Abs(($ts[$i] - $t0).TotalMilliseconds); if ($d -lt $bd) { $bd = $d; $best = $i } }
  [void]$img.SelectActiveFrame($dim, $best)
  $w = [int]($img.Width * $h / $img.Height); $t = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($t); $g.DrawImage($img, 0, 0, $w, $h)
  $g.FillRectangle([System.Drawing.Brushes]::Black, 0, 0, $w, 20)
  $g.DrawString("$($rows[$best][0])", (New-Object System.Drawing.Font 'Segoe UI', 9), [System.Drawing.Brushes]::White, 3, 2); $g.Dispose(); $tiles += $t
}
$W = 0; foreach ($t in $tiles) { $W += $t.Width + 4 }
$s = New-Object System.Drawing.Bitmap $W, $h; $g = [System.Drawing.Graphics]::FromImage($s); $g.Clear([System.Drawing.Color]::FromArgb(40, 40, 40)); $x = 0
foreach ($t in $tiles) { $g.DrawImage($t, $x, 0); $x += $t.Width + 4 }
$enc = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
$ep = New-Object System.Drawing.Imaging.EncoderParameters 1; $ep.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter([System.Drawing.Imaging.Encoder]::Quality, [long]75)
$s.Save($Out, $enc, $ep); "$W x $h"
