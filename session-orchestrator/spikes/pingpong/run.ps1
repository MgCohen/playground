# Starts the Windows parts of the spike. Each id goes to pids.txt in the data folder, so you can end only these processes.
param([string]$Data = "$env:TEMP\pingpong", [switch]$Share, [string[]]$Heads = @("Engine", "Web", "Widget", "Notify"))
$root = $PSScriptRoot
New-Item -ItemType Directory -Force $Data | Out-Null
$env:PP_DATA = $Data
$env:PP_SHARE = if ($Share) { "1" } else { "0" }
$env:PP_ENGINE = "http://127.0.0.1:7931/"
$env:PP_APP = "$root\PingPong.App\bin\Release\net10.0-windows\PingPong.App.exe"
foreach ($h in $Heads) {
    $tfm = if ($h -in "Engine", "Web") { "net10.0" } else { "net10.0-windows" }
    $dir = "$root\PingPong.$h\bin\Release\$tfm"
    $opt = @{ FilePath = "$dir\PingPong.$h.exe"; WorkingDirectory = $dir; PassThru = $true; RedirectStandardError = "$Data\$h.err.txt"; RedirectStandardOutput = "$Data\$h.out.txt" }
    if ($h -eq "Widget") { $opt.ArgumentList = "--backdrop" }
    if ($h -in "Engine", "Web") { $opt.WindowStyle = "Hidden" }
    $p = Start-Process @opt
    "$h $($p.Id)" | Tee-Object -Append "$Data\pids.txt"
}
