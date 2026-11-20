# Usage: sheets.sh <name> [css]: the sheet stress (window message clicks), recorded at 30 fps, then the empty frame count.
# The results go to $OUT (default: ./out). The tray takes a free test slot and the recorder records the area of that slot
# (see scenario.sh). Exit codes: 0 done; 1 the tray failed (log in <name>.tray.txt); 3 or 4 the recorder failed (<name>.rec.txt).
S="${OUT:-$(pwd)/out}"; mkdir -p "$S"; D="$(cd "$(dirname "$0")" && pwd)"
T="$D/../../.."
name=$1; css=$2
rm -rf "$S/$name" "$S/$name.gif" "$S/$name.gif.times.txt" "$S/$name.rec.txt" "$S/$name.tray.txt"
mkdir -p "$S/$name"
(cd "$T/spikes/tray-display/recorder" && dotnet run --no-build -- --area-file "$S/$name/instance.json" 0.2 30 66 "$S/$name.gif" > "$S/$name.rec.txt" 2>&1) &
recorder=$!
cd "$T/src/Orchestrator.Tray"
if [ -n "$css" ]; then timeout 240 dotnet run --no-build -- --selftest "$S/$name" --demo --stress --sheets --css "$css" --wait-recorder > "$S/$name.tray.txt" 2>&1
else timeout 240 dotnet run --no-build -- --selftest "$S/$name" --demo --stress --sheets --wait-recorder > "$S/$name.tray.txt" 2>&1; fi
tray=$?
wait $recorder
rec=$?
if [ $tray -ne 0 ]; then echo "sheets.sh: the tray failed with exit code $tray. Log: $S/$name.tray.txt" >&2; exit 1; fi
if [ $rec -ne 0 ]; then echo "sheets.sh: the recorder failed with exit code $rec. Log: $S/$name.rec.txt" >&2; exit $rec; fi
echo "$name: $(python "$D/empty.py" "$S/$name.gif.times.txt" "$S/$name/stress.txt" 60 | head -1)"
python "$D/empty.py" "$S/$name.gif.times.txt" "$S/$name/stress.txt" 60 | tail -n +2
