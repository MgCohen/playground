# Usage: empty.py <times file> <step log> <seconds> [key [limit]]: the empty frames, and the step before each one.
# With "key" (I2): the recording used --backdrop-key, and the 3rd column of the times file is the share (0 to 100) of the pixels
# that are exactly the key color. A frame is empty when that share is above the limit (default 98), the frame before it was
# not empty, and a later frame shows the tray again: a blink. The end of the run (the tray closes) is no blink. It also prints the highest share of a frame that is not empty, to show the margin to the limit.
# With no "key" (old rule): a frame is empty when its mean is within 0.6 of the mean of the first frame (the grey backdrop).
import sys, datetime
T = lambda x: datetime.datetime.strptime(x, "%H:%M:%S.%f")
rows = [l.split() for l in open(sys.argv[1]).read().splitlines() if l.strip()]
import re
# The step lines start with a time; the press summary at the end of the log (I3) does not.
steps = [(T(l[:12]), l[13:]) for l in open(sys.argv[2], encoding="utf-8").read().splitlines() if re.match(r"\d\d:\d\d:\d\d\.\d{3} ", l)]
t0, span = steps[0][0], float(sys.argv[3])
key = len(sys.argv) > 4 and sys.argv[4] == "key"
limit = float(sys.argv[5]) if len(sys.argv) > 5 else 98.0
empty = float(rows[0][1])
found, highest = [], 0.0
for i in range(1, len(rows)):
    t = T(rows[i][0])
    if not 0 <= (t - t0).total_seconds() <= span: continue
    if key:
        v, prev = float(rows[i][2]), float(rows[i - 1][2])
        blank = v > limit and prev <= limit and any(float(r[2]) <= limit for r in rows[i + 1:])
        if v <= limit: highest = max(highest, v)
    else:
        v, prev = float(rows[i][1]), float(rows[i - 1][1])
        blank = abs(v - empty) < 0.6 and abs(prev - empty) > 2
    if blank:
        before = [s for s in steps if s[0] <= t]
        found.append(f"{rows[i][0]}  {(t - before[-1][0]).total_seconds() * 1000:5.0f} ms after {before[-1][1][:45]}  ({v:.2f})")
print(f"{len(found)} empty frames in {span:.0f} s" + (f" (key share above {limit:g} %; highest share of a tray frame {highest:.2f} %)" if key else ""))
for f in found: print("  " + f)
