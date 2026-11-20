# Usage: scenario.sh <scenario> <out name> [seconds]: one scenario of the tray (window messages only), recorded at 30 fps,
# then an MP4. The results go to $OUT (default: ./out). The recorder area is the panel place on a 2560 x 1600 screen.
# Exit codes: 0 done; 1 the tray failed (log in <name>.tray.txt); 3 the recorder caught no frame (log in <name>.rec.txt).
S="${OUT:-$(pwd)/out}"; mkdir -p "$S"; D="$(cd "$(dirname "$0")" && pwd)"
T="$D/../../.."
scenario=$1; name=$2; secs=${3:-14}
# I17: each run has its own tray instance (own lock and WebView2 folder), so a self-test tray of another session
# does not stop this tray. Without it, the tray exited at once and the recorder caught 0 frames.
instance="scenario-$$"
rm -rf "$S/$name" "$S/$name.gif" "$S/$name.gif.times.txt" "$S/$name.rec.txt" "$S/$name.tray.txt"
(cd "$T/spikes/tray-display/recorder" && dotnet run --no-build -- 1640 160 920 1368 0.5 30 "$secs" "$S/$name.gif" > "$S/$name.rec.txt" 2>&1) &
recorder=$!
sleep 3
(cd "$T/src/Orchestrator.Tray" && timeout 60 dotnet run --no-build -- --selftest "$S/$name" --scenario "$scenario" --instance "$instance" > "$S/$name.tray.txt" 2>&1)
tray=$?
wait $recorder
rec=$?
rm -rf "${LOCALAPPDATA:-$HOME/AppData/Local}/Orchestrator.Tray/WebView2.selftest.$instance"
head -1 "$S/$name.rec.txt"
if [ $tray -ne 0 ]; then
  echo "scenario.sh: the tray failed with exit code $tray. Log: $S/$name.tray.txt" >&2
  tail -5 "$S/$name.tray.txt" >&2
  exit 1
fi
if [ $rec -ne 0 ]; then
  echo "scenario.sh: the recorder failed with exit code $rec. Log: $S/$name.rec.txt" >&2
  exit $rec
fi
(cd "$T/spikes/tray-display/recorder" && dotnet run --no-build -- mp4 "$S/$name.gif" "$S/$name.mp4" 30 | tail -1)
cat "$S/$name/scenario.txt"
