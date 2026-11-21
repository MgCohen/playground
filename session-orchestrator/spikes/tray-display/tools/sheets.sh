# Usage: sheets.sh <name> [css]: the sheet stress (window message clicks), recorded at 30 fps, then the empty frame count.
# The results go to $OUT (default: ./out). The tray takes a free test slot and the recorder records the area of that slot
# (see scenario.sh). I2: the empty frame count uses the key backdrop (--backdrop-key, #FF00FF) and the share of key pixels of
# each frame; LIMIT sets the share limit (default 98). Set INSTANCE (for example INSTANCE=I2) to run the built exe with
# --instance; else dotnet run --no-build. Exit codes: 0 done; 1 the tray failed (log in <name>.tray.txt); 3 or 4 the recorder
# failed (<name>.rec.txt). The tray exit code comes after the count.
S="${OUT:-$(pwd)/out}"; mkdir -p "$S"; D="$(cd "$(dirname "$0")" && pwd)"
T="$D/../../.."
name=$1; css=$2
rm -rf "$S/$name" "$S/$name.gif" "$S/$name.gif.times.txt" "$S/$name.rec.txt" "$S/$name.tray.txt"
mkdir -p "$S/$name"
(cd "$T/spikes/tray-display/recorder" && dotnet run --no-build -- --backdrop-key --area-file "$S/$name/instance.json" 0.2 30 66 "$S/$name.gif" > "$S/$name.rec.txt" 2>&1) &
recorder=$!
cd "$T/src/Orchestrator.Tray"
run=(--selftest "$S/$name" --demo --stress --sheets --wait-recorder)
if [ -n "$css" ]; then run+=(--css "$css"); fi
if [ -n "$INSTANCE" ]; then timeout 240 "$T/artifacts~/bin/Orchestrator.Tray/debug/Orchestrator.Tray.exe" --instance "$INSTANCE" "${run[@]}" > "$S/$name.tray.txt" 2>&1
else timeout 240 dotnet run --no-build -- "${run[@]}" > "$S/$name.tray.txt" 2>&1; fi
tray=$?
wait $recorder
rec=$?
if [ $rec -ne 0 ]; then echo "sheets.sh: the recorder failed with exit code $rec. Log: $S/$name.rec.txt" >&2; exit $rec; fi
# A press miss (I3) gives the tray exit code 1, but the recording is complete: count the empty frames, then fail.
python "$D/empty.py" "$S/$name.gif.times.txt" "$S/$name/stress.txt" 60 key "${LIMIT:-98}" | sed "1s/^/$name: /"
if [ $tray -ne 0 ]; then echo "sheets.sh: the tray failed with exit code $tray. Log: $S/$name.tray.txt" >&2; exit 1; fi
