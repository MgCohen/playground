# Usage: win.ps1 list <pid> | move <hwnd> <x> <y> | min <hwnd> | restore <hwnd>. Window messages and SetWindowPos only; no pointer.
$cmd, $a, $b, $c = $args
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class W {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  public struct RECT { public int L, T, R, B; }
}
"@
switch ($cmd) {
  'list' { $script:rows = @()
    [W]::EnumWindows({ param($h, $l) $p = 0; [void][W]::GetWindowThreadProcessId($h, [ref]$p)
      if ($p -eq [int]$a) { $t = New-Object Text.StringBuilder 256; $k = New-Object Text.StringBuilder 256; [void][W]::GetWindowText($h, $t, 256); [void][W]::GetClassName($h, $k, 256)
        $r = New-Object W+RECT; [void][W]::GetWindowRect($h, [ref]$r); $ex = [W]::GetWindowLong($h, -20)
        $script:rows += "{0:x} vis={1} ex=0x{2:x} '{3}' [{4}] {5},{6} {7}x{8}" -f [int64]$h, [W]::IsWindowVisible($h), $ex, $t, $k, $r.L, $r.T, ($r.R-$r.L), ($r.B-$r.T) }
      $true }, [IntPtr]::Zero) | Out-Null; $script:rows }
  'move' { [W]::SetWindowPos([IntPtr][Convert]::ToInt64($a,16), [IntPtr]::Zero, [int]$b, [int]$c, 0, 0, 0x15) }
  'top' { [W]::SetWindowPos([IntPtr][Convert]::ToInt64($a,16), [IntPtr](-1), 0, 0, 0, 0, 0x13) }
  'notop' { [W]::SetWindowPos([IntPtr][Convert]::ToInt64($a,16), [IntPtr](-2), 0, 0, 0, 0, 0x13) }
  'min' { [W]::ShowWindow([IntPtr][Convert]::ToInt64($a,16), 6) }
  'restore' { [W]::ShowWindow([IntPtr][Convert]::ToInt64($a,16), 4) }
}
