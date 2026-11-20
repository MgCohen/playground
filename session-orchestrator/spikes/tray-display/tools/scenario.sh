# Usage: scenario.sh <scenario> <out name> [seconds]: one scenario of the tray (window messages only), recorded at 30 fps,
# then an MP4. The results go to $OUT (default: ./out). The recorder area is the panel place on a 2560 x 1600 screen.
S="${OUT:-$(pwd)/out}"; mkdir -p "$S"; D="$(cd "$(dirname "$0")" && pwd)"
T="$D/../../.."
scenario=$1; name=$2; secs=${3:-14}
rm -rf "$S/$name" "$S/$name.gif" "$S/$name.gif.times.txt"
(cd "$T/spikes/tray-display/recorder" && dotnet run --no-build -- 1640 160 920 1368 0.5 30 "$secs" "$S/$name.gif" > "$S/$name.rec.txt" 2>&1) &
sleep 3
(cd "$T/src/Orchestrator.Tray" && timeout 60 dotnet run --no-build -- --selftest "$S/$name" --scenario "$scenario" > /dev/null 2>&1)
wait
head -1 "$S/$name.rec.txt"
(cd "$T/spikes/tray-display/recorder" && dotnet run --no-build -- mp4 "$S/$name.gif" "$S/$name.mp4" 30 | tail -1)
cat "$S/$name/scenario.txt"
