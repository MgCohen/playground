# Usage: runs.sh [day]: the test runs of the tray and the recorder, from the run records in %LOCALAPPDATA%\Orchestrator.Tray\runs.
# Day: yyyymmdd (default: today). Shows the number of runs and the total time, by code and by kind. It only reads the files.
day=${1:-$(date +%Y%m%d)}
dir="${LOCALAPPDATA:-$HOME/AppData/Local}/Orchestrator.Tray/runs"
python - "$dir" "$day" <<'PY'
import sys, os, json, collections
folder, day = sys.argv[1], sys.argv[2]
rows = []
for f in sorted(os.listdir(folder)) if os.path.isdir(folder) else []:
    if f.startswith(day) and f.endswith(".json"):
        try: rows.append(json.load(open(os.path.join(folder, f), encoding="utf-8")))
        except (OSError, ValueError): print(f"skipped (cannot read): {f}")
trays = [r for r in rows if r.get("source") == "tray"]
recs = {r["run"]: r for r in rows if r.get("source") == "recorder"}
print(f"{day}: {len(trays)} tray runs, {sum(r['seconds'] for r in trays):.0f} s; {len(recs)} recordings, {sum(r['seconds'] for r in recs.values()):.0f} s")
groups = collections.defaultdict(list)
for r in trays: groups[(r.get("code"), r.get("kind"))].append(r)
print(f"{'code':<12} {'kind':<15} {'runs':>4} {'failed':>6} {'time s':>7}")
for (code, kind), g in sorted(groups.items(), key=lambda x: (str(x[0][0]), str(x[0][1]))):
    print(f"{str(code):<12} {str(kind):<15} {len(g):>4} {sum(1 for r in g if r.get('result')):>6} {sum(r['seconds'] for r in g):>7.0f}")
zero = [r for r in recs.values() if r.get("frames") == 0]
if zero: print(f"recordings with 0 frames: {', '.join(r['run'] for r in zero)}")
PY
