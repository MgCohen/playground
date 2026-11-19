# Usage: sheets.sh <name> [css]: the sheet stress (window message clicks), recorded at 30 fps, then the empty frame count.
# The results go to $OUT (default: ./out). The recorder area is the panel place on a 2560 x 1600 screen.
S="${OUT:-$(pwd)/out}"; mkdir -p "$S"; D="$(cd "$(dirname "$0")" && pwd)"
T="$D/../../.."
name=$1; css=$2
(cd "$T/spikes/tray-display/recorder" && dotnet run --no-build -- 1640 160 920 1368 0.2 30 66 "$S/$name.gif" > "$S/$name.rec.txt" 2>&1) &
sleep 4
rm -rf "$S/$name"
cd "$T/src/Orchestrator.Tray"
if [ -n "$css" ]; then timeout 120 dotnet run --no-build -- --selftest "$S/$name" --demo --stress --sheets --css "$css" >/dev/null 2>&1
else timeout 120 dotnet run --no-build -- --selftest "$S/$name" --demo --stress --sheets >/dev/null 2>&1; fi
wait
echo "$name: $(python "$D/empty.py" "$S/$name.gif.times.txt" "$S/$name/stress.txt" 60 | head -1)"
python "$D/empty.py" "$S/$name.gif.times.txt" "$S/$name/stress.txt" 60 | tail -n +2
