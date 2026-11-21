# Usage: cover.ps1 <hwnd hex>: a red topmost window that follows the rectangle of that window for 7 s.
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class R { public struct RECT { public int L, T, R2, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); }
"@
$h = [IntPtr][Convert]::ToInt64($args[0], 16)
$f = New-Object Windows.Forms.Form
$f.FormBorderStyle = 'None'; $f.StartPosition = 'Manual'; $f.TopMost = $true; $f.ShowInTaskbar = $false; $f.BackColor = [Drawing.Color]::Red
function Follow { $r = New-Object R+RECT; [void][R]::GetWindowRect($h, [ref]$r); $f.Bounds = New-Object Drawing.Rectangle(($r.L - 10), ($r.T - 10), ($r.R2 - $r.L + 20), ($r.B - $r.T + 20)) }
Follow
$t = New-Object Windows.Forms.Timer; $t.Interval = 100; $n = 0
$t.Add_Tick({ Follow; $script:n++; if ($script:n -gt 70) { $f.Close() } }); $t.Start()
[Windows.Forms.Application]::Run($f)
