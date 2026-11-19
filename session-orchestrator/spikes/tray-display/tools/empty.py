# Usage: empty.py <times file> <step log> <seconds>: the empty frames from the frame means, and the step before each one.
import sys, datetime
T = lambda x: datetime.datetime.strptime(x, "%H:%M:%S.%f")
rows = [l.split() for l in open(sys.argv[1]).read().splitlines() if l.strip()]
steps = [(T(l[:12]), l[13:]) for l in open(sys.argv[2], encoding="utf-8").read().splitlines()]
t0, span = steps[0][0], float(sys.argv[3])
empty = float(rows[0][1])
found = []
for i in range(1, len(rows)):
    t, v, prev = T(rows[i][0]), float(rows[i][1]), float(rows[i - 1][1])
    if not 0 <= (t - t0).total_seconds() <= span: continue
    if abs(v - empty) < 0.6 and abs(prev - empty) > 2:
        before = [s for s in steps if s[0] <= t]
        found.append(f"{rows[i][0]}  {(t - before[-1][0]).total_seconds() * 1000:5.0f} ms after {before[-1][1][:45]}")
print(f"{len(found)} empty frames in {span:.0f} s")
for f in found: print("  " + f)
