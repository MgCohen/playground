"""Print one line only when the root's children change. Read only. Arg 'once' prints the snapshot."""
import subprocess, sys, re, json, glob, os, time, shutil
from datetime import datetime, timezone
sys.stdout.reconfigure(encoding='utf-8')
ORCH = shutil.which('orch'); GH = shutil.which('gh')
REPO = r'C:\Users\matheus.cohen\Documents\Projects\playground'
PROJ = os.path.expanduser('~/.claude/projects')

def run(*a):
    r = subprocess.run(a, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=60, cwd=REPO)
    if r.returncode: raise RuntimeError(f'{os.path.basename(a[0])} {a[1]}: {r.stderr.strip()[:150]}')
    return r.stdout

def last_message(sid):
    fs = glob.glob(f'{PROJ}/*/{sid}*.jsonl')
    if not fs: return None, False
    last = None
    for l in open(max(fs, key=os.path.getmtime), encoding='utf-8', errors='replace'):
        if '"type":"user"' not in l and '"type":"assistant"' not in l: continue
        try: d = json.loads(l)
        except ValueError: continue
        if d.get('type') in ('user', 'assistant') and not d.get('isSidechain') and d.get('message'): last = d
    if not last: return None, False
    ended = last['type'] == 'assistant' and last['message'].get('stop_reason') in ('end_turn', 'stop_sequence')
    age = (datetime.now(timezone.utc) - datetime.fromisoformat(last['timestamp'].replace('Z', '+00:00'))).total_seconds()
    working = not ended and age < 900
    return (None if working else last.get('uuid')), working

def snap():
    s = {}
    for l in run(ORCH, 'questions').splitlines():
        m = re.match(r'(toolu_\S+) \[\w+\] (.*)', l)
        if m: s['question ' + m[1]] = m[2][:160]
    tree = run(ORCH, 'tree').splitlines()
    inside = seen = False
    for l in tree:
        if re.match(r'\S', l): inside = l.startswith('ab9acae5'); seen |= inside; continue
        m = re.match(r'  ([0-9a-f]{8}) (.*) \[(.*)\]$', l)
        if not inside or not m or 'archived' in m[3]: continue
        name = m[2].split(':')[0]
        rep = re.findall(r'^  report: (.*)', run(ORCH, 'status', m[1]), re.M)
        msg, work = last_message(m[1])
        s['report ' + name] = (rep or ['none'])[0]
        s['state ' + name] = 'working' if work else 'idle'
        if not work: s['message ' + name] = msg
    if not seen: raise RuntimeError('orch tree has no root ab9acae5')
    for p in json.loads(run(GH, 'pr', 'list', '--state', 'all', '--limit', '100', '--json', 'number,state,isDraft,title')):
        s[f"PR #{p['number']}"] = f"{p['state']}{' draft' if p['isDraft'] else ''} {p['title'][:80]}"
    return s

def counts(s):
    n = lambda f: sum(1 for k, v in s.items() if f(k, v))
    return (f"{n(lambda k,v:k.startswith('question'))} in queue, {n(lambda k,v:k.startswith('state') and v=='working')} working, "
            f"{n(lambda k,v:k.startswith('state') and v=='idle')} idle")

def diff(a, b):
    out = []
    for k in sorted(set(a) | set(b)):
        if a.get(k) == b.get(k): continue
        if k.startswith('message'):
            if k in b and k in a or a.get('state ' + k[8:]) == 'working' and k in b: out.append('NEW ' + k)
        elif k not in b: out.append('GONE ' + k)
        else: out.append(f"{'NEW' if k not in a else 'CHANGED'} {k}: {b[k]}")
    return out

if sys.argv[1:] == ['once']:
    s = snap(); print(counts(s)); [print(k, '=', v) for k, v in s.items() if not k.startswith('PR')]; sys.exit()
STATE = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'watch-state.json')
def save(s): json.dump(s, open(STATE, 'w', encoding='utf-8'))
prev = json.load(open(STATE, encoding='utf-8')) if os.path.exists(STATE) else None
fails = 0
while True:
    try: cur = snap(); fails = 0
    except Exception as e:
        fails += 1
        if fails == 3: print(f'WATCH FAILS 3 times: {e}'); sys.exit(1)
        time.sleep(120); continue
    d = diff(prev, cur) if prev is not None else []
    save(cur); prev = cur
    if d: print(counts(cur), *d, sep=chr(10)); sys.exit()
    time.sleep(120)
