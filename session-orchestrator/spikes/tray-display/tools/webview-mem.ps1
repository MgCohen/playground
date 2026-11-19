# Usage: webview-mem.ps1 <part of the user data folder>: the WebView2 processes of that folder, with their type and memory (MB).
param([string]$Folder = "WebView2.selftest")
$rows = Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object { $_.CommandLine -like "*$Folder*" } | ForEach-Object {
  $type = if ($_.CommandLine -match '--type=([a-z-]+)') { $matches[1] } else { 'browser' }
  if ($_.CommandLine -match '--utility-sub-type=([A-Za-z0-9._-]+)') { $type = $matches[1] -replace '\.mojom\..*', '' }
  $p = Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue
  if ($p) { [pscustomobject]@{ Type = $type; Pid = $p.Id; WorkingSetMb = [int]($p.WorkingSet64 / 1MB); PrivateMb = [int]($p.PrivateMemorySize64 / 1MB) } }
}
$rows | Sort-Object Type | Format-Table -AutoSize | Out-String -Width 200
"total: {0} processes, working set {1} MB, private {2} MB" -f $rows.Count, ($rows | Measure-Object WorkingSetMb -Sum).Sum, ($rows | Measure-Object PrivateMb -Sum).Sum
