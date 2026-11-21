# Usage: scenario.sh <scenario> <out name> [seconds]: one scenario of the tray (window messages only), recorded at 30 fps,
# then an MP4. The results go to $OUT (default: ./out).
# The tray takes a free test slot (its own lock, WebView2 folder and strip of the screen) and writes <name>/instance.json.
# The recorder waits for that file and records the area of the slot. The tray waits for the recorder (--wait-recorder).
# So two sessions can run this script at the same time.
# Set RUN_CODE (for example RUN_CODE=I17) for the label of the tray: "I17 · <build>". Each run writes a run record (tools/runs.sh).
# No backdrop by default: the screen stays as it is. Set BACKDROP=1 for a grey backdrop (only for a check that needs it, a shadow at the edge).
# Exit codes: 0 done; 1 the tray failed (logs in <name>/console.txt and <name>/scenario.txt); 3 the recorder caught no frame, 4 no area (log in <name>.rec.txt).
S="${OUT:-$(pwd)/out}"; mkdir -p "$S"; D="$(cd "$(dirname "$0")" && pwd)"
T="$D/../../.."
scenario=$1; name=$2; secs=${3:-14}
rm -rf "$S/$name" "$S/$name.gif" "$S/$name.gif.times.txt" "$S/$name.rec.txt" "$S/$name.tray.txt"
mkdir -p "$S/$name"
(cd "$T/spikes/tray-display/recorder" && dotnet run --no-build -- ${BACKDROP:+--backdrop} --area-file "$S/$name/instance.json" 0.5 30 "$secs" "$S/$name.gif" > "$S/$name.rec.txt" 2>&1) &
recorder=$!
(cd "$T/src/Orchestrator.Tray" && timeout 240 dotnet run --no-build -- --selftest "$S/$name" --scenario "$scenario" --wait-recorder > "$S/$name.tray.txt" 2>&1)
tray=$?
wait $recorder
rec=$?
grep -m1 "test slot" "$S/$name.tray.txt"
grep -m1 "frames in" "$S/$name.rec.txt"
if [ $tray -ne 0 ]; then
  echo "scenario.sh: the tray failed with exit code $tray. Logs: $S/$name/console.txt, $S/$name/scenario.txt" >&2
  tail -5 "$S/$name/console.txt" "$S/$name/scenario.txt" 2>/dev/null >&2
  exit 1
fi
if [ $rec -ne 0 ]; then
  echo "scenario.sh: the recorder failed with exit code $rec. Log: $S/$name.rec.txt" >&2
  tail -2 "$S/$name.rec.txt" >&2
  exit $rec
fi
(cd "$T/spikes/tray-display/recorder" && dotnet run --no-build -- mp4 "$S/$name.gif" "$S/$name.mp4" 30 | tail -1)
cat "$S/$name/scenario.txt"
