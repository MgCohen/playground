# Usage: run-all.sh <tray exe> <out folder>: the 5 window capture checks on a new tray (--instance wgcspike).
# Window calls only; the pointer does not move. Build capture/ first (dotnet build -c Release -o bin).
D="$(cd "$(dirname "$0")" && pwd)"; EXE=$1; O=$2
W="$D/../capture/bin/capture.exe"; WIN="powershell -NoProfile -File $D/win.ps1"; rm -rf "$O"; mkdir -p "$O"
"$EXE" --instance wgcspike >/dev/null 2>&1 &
sleep 3
PID=$(powershell -NoProfile -Command "(Get-Process Orchestrator.Tray | Where-Object { \$_.Path -eq '$(cygpath -w "$EXE")' }).Id")
for i in $(seq 1 30); do H=$($WIN list $PID | grep "vis=True" | grep "'Orchestrator' " | cut -d' ' -f1); [ -n "$H" ] && break; sleep 1; done
echo "tray pid $PID hwnd $H"; sleep 2
echo "== 1 plain capture"; "$W" $H "$O/1-plain.png" 0
$WIN top $H >/dev/null
echo "== 5a border on, tray on top"; "$W" $H "$O/5a-border-on.png" 1
echo "== 5b border off, tray on top"; "$W" $H "$O/5b-border-off.png" 0
echo "== 2 tray covered by a red window"; (powershell -NoProfile -File "$D/cover.ps1" $H &); sleep 3; "$W" $H "$O/2-covered.png" 0; sleep 5
echo "== 3 tray off the screen (x = -3000)"; $WIN move $H -3000 300 >/dev/null; sleep 1; "$W" $H "$O/3-offscreen.png" 0
echo "== 4 tray minimized"; $WIN move $H 1300 260 >/dev/null; sleep 1; $WIN min $H >/dev/null; sleep 1; "$W" $H "$O/4-minimized.png" 0 5000
echo "== stop tray"; taskkill //PID $PID //F
