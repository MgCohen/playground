import json, os
from xml.sax.saxutils import escape

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "r5")
os.makedirs(OUT, exist_ok=True)

CSS = """
.bg{fill:#fdfdfc}.soft{fill:#f1eee9}.t{fill:#2a2825}.m{fill:#6b665f}
.ln{stroke:rgba(27,26,24,.14);fill:none}.lnF{fill:rgba(27,26,24,.14)}.lnS{stroke:rgba(27,26,24,.22)}
.edge{stroke:#8d877e;fill:none;stroke-width:1.6}
.efut{stroke:#8d877e;fill:none;stroke-width:1.4;stroke-dasharray:2 3;opacity:.5}
.loop{stroke:#c2410c;fill:none;stroke-width:1.6;stroke-dasharray:4 3}
.loopF{fill:#c2410c}.loopS{stroke:#c2410c}
.enext{stroke:#7c4dff;fill:none;stroke-width:1.6;stroke-dasharray:4 3}
.agent{fill:#1f5fbf}.human{fill:#ff6a1a}.tool{fill:#6b665f}.humanT{fill:#c2410c}.humanS{stroke:#ff6a1a}
.childS{stroke:#2d6a4f}.childF{fill:#2d6a4f}.childSoft{fill:rgba(45,106,79,.10)}
.exitS{stroke:#5d6660}.exitF{fill:#5d6660}
.passS{stroke:#2f8f5b}.passF{fill:#2f8f5b}.failS{stroke:#c23b22}.failF{fill:#c23b22}
.nowS{stroke:#7c4dff}.nowF{fill:#7c4dff}.nowSoft{fill:rgba(124,77,255,.12)}
.selS{stroke:#1f5fbf}.selF{fill:#1f5fbf}.selSoft{fill:rgba(31,95,191,.10)}
.futS{stroke:#8d877e}
.dim{fill:rgba(27,26,24,.30)}
.shadow{fill:rgba(27,26,24,.10)}
.sans{font-family:Inter,system-ui,sans-serif}.mono{font-family:ui-monospace,monospace}
@media (prefers-color-scheme: dark){
.bg{fill:#262522}.soft{fill:#2e2c29}.t{fill:#ece9e4}.m{fill:#a39d94}
.ln{stroke:rgba(255,255,255,.12)}.lnF{fill:rgba(255,255,255,.12)}.lnS{stroke:rgba(255,255,255,.2)}
.edge{stroke:#8a847b}.efut{stroke:#8a847b}.futS{stroke:#8a847b}
.loop{stroke:#ff8a4c}.loopF{fill:#ff8a4c}.loopS{stroke:#ff8a4c}
.enext{stroke:#a98bff}
.agent{fill:#6ea0ff}.human{fill:#ff7a33}.tool{fill:#a39d94}.humanT{fill:#ff7a33}.humanS{stroke:#ff7a33}
.childS{stroke:#6fcf97}.childF{fill:#6fcf97}.childSoft{fill:rgba(111,207,151,.12)}
.exitS{stroke:#9ba59e}.exitF{fill:#9ba59e}
.passS{stroke:#5cc28a}.passF{fill:#5cc28a}.failS{stroke:#ff7a66}.failF{fill:#ff7a66}
.nowS{stroke:#a98bff}.nowF{fill:#a98bff}.nowSoft{fill:rgba(169,139,255,.16)}
.selS{stroke:#6ea0ff}.selF{fill:#6ea0ff}.selSoft{fill:rgba(110,160,255,.14)}
.dim{fill:rgba(0,0,0,.42)}
.shadow{fill:rgba(0,0,0,.35)}
}
"""


def tw(s, fs, mono=False, bold=False):
    w = 0.0
    for ch in s:
        if mono:
            w += 0.61
        elif ch in "il.,:'|!·ftj ":
            w += 0.32
        elif ch in "mwMW":
            w += 0.85
        elif ch.isupper() or ch.isdigit():
            w += 0.64
        else:
            w += 0.55
    return w * fs * (1.05 if bold and not mono else 1.0)


class SVG:
    def __init__(self, clip_top=0):
        self.under, self.edges, self.top, self.chrome = [], [], [], []
        self.issues = []
        self.clip_top = clip_top
        self.cur = self.top
        self.vis = (clip_top, 720)

    def text(self, x, y, s, fs, cls, mono=False, bold=False, anchor="start", maxx=352, minx=8, layer=None):
        w = tw(s, fs, mono, bold)
        x0 = x - w if anchor == "end" else (x - w / 2 if anchor == "middle" else x)
        if x0 + w > maxx + 0.5 or x0 < minx - 0.5:
            self.issues.append(f"overflow: {s!r} x0={x0:.0f} end={x0+w:.0f} max={maxx}")
        fam = "mono" if mono else "sans"
        a = f' text-anchor="{anchor}"' if anchor != "start" else ""
        b = ' font-weight="600"' if bold else ""
        (layer if layer is not None else self.cur).append(
            f'<text x="{x:.1f}" y="{y:.1f}" font-size="{fs}" class="{cls} {fam}"{a}{b}>{escape(s)}</text>')
        return w

    def raw(self, s, layer=None):
        (self.cur if layer is None else getattr(self, layer)).append(s)

    def out(self):
        ct = self.clip_top
        return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 360 720" width="360" height="720">'
                f'<style>{CSS}</style>'
                f'<defs><clipPath id="scroll"><rect x="0" y="{ct}" width="360" height="{720-ct}"/></clipPath></defs>'
                '<rect width="360" height="720" class="bg"/>'
                '<g clip-path="url(#scroll)">'
                + "".join(self.under) + "".join(self.edges) + "".join(self.top) + "</g>"
                + "".join(self.chrome) + "</svg>")


def chrome(S):
    S.cur = S.chrome


def tree(S):
    S.cur = S.top


def rect(S, x, y, w, h, rx, cls, extra=""):
    S.raw(f'<rect x="{x:.1f}" y="{y:.1f}" width="{w:.1f}" height="{h:.1f}" rx="{rx}" class="{cls}"{extra}/>')


def frame(S, x, y, w, h, rx, sc, sw=1.2, dash=None):
    d = f' stroke-dasharray="{dash}"' if dash else ""
    S.raw(f'<rect x="{x:.1f}" y="{y:.1f}" width="{w:.1f}" height="{h:.1f}" rx="{rx}" fill="none" class="{sc}" stroke-width="{sw}"{d}/>')


def hline(S, x1, x2, y):
    S.raw(f'<path d="M{x1} {y}H{x2}" class="ln"/>')


def you_pill(S, x, y, fs=9):
    """y = baseline. Returns the width."""
    w = tw("you", fs, mono=True) + 10
    rect(S, x, y - 10, w, 13, 6.5, "human")
    S.text(x + 5, y, "you", fs, "bg", mono=True, bold=True)
    return w


def chip(S, x, y, label, sc, tc, fs=9, h=13):
    """y = baseline. Returns the width."""
    w = tw(label, fs, mono=True) + 10
    S.raw(f'<rect x="{x:.1f}" y="{y-h+3.5:.1f}" width="{w:.1f}" height="{h}" rx="{h/2}" class="bg"/>')
    frame(S, x, y - h + 3.5, w, h, h / 2, sc, 1.1)
    S.text(x + 5, y, label, fs, tc, mono=True)
    return w


def button(S, x, y, w, label, kind, h=34, fs=11.5):
    if kind == "pri":
        rect(S, x, y, w, h, 9, "nowF")
        S.text(x + w / 2, y + h / 2 + 4, label, fs, "bg", bold=True, anchor="middle", maxx=x + w)
    else:
        rect(S, x, y, w, h, 9, "bg")
        frame(S, x, y, w, h, 9, "nowS", 1.3)
        S.text(x + w / 2, y + h / 2 + 4, label, fs, "nowF", bold=True, anchor="middle", maxx=x + w)


def buttons(S, x, y, w, labels, h=32, gap=8, fs=11):
    """Width of each button follows its label."""
    nat = [tw(l, fs, bold=True) + 20 for l in labels]
    extra = (w - sum(nat) - gap * (len(labels) - 1)) / len(labels)
    if extra < 0:
        S.issues.append(f"buttons too wide {labels}")
    xx = x
    for i, l in enumerate(labels):
        bw = nat[i] + extra
        button(S, xx, y, bw, l, "pri" if i == 0 else "sec", h, fs)
        xx += bw + gap


def link(S, x, y, label, fs=11, maxx=344):
    w = S.text(x, y, label, fs, "selF", maxx=maxx - 10)
    S.text(x + w * 0.9 + 4, y, "›", fs + 1, "selF", maxx=maxx)


# ------------------------------------------------------------ run page header
def header(S, title, elapsed, session, crumb=None, right3=None, menu=False):
    chrome(S)
    S.raw('<path d="M22 18L15 26L22 34" class="edge" stroke-width="2"/>')
    S.text(38, 26, title, 15, "t", bold=True, maxx=270)
    S.text(316, 26, elapsed, 11, "m", mono=True, anchor="end")
    if menu:
        S.raw('<circle cx="337" cy="22" r="13" class="soft"/>')
    for dx in (-5, 0, 5):
        S.raw(f'<circle cx="{337+dx}" cy="22" r="1.8" class="t"/>')
    S.raw('<circle cx="42" cy="40.5" r="3.5" class="human"/>')
    w = S.text(50, 44, "Waits for you", 10.5, "humanT", bold=True)
    x = 50 + w + 8
    x += S.text(x, 44, "session", 10, "m") + 4
    link(S, x, 44, session, 10, 348)
    bottom = 52
    if crumb:
        x = 38
        for i, part in enumerate(crumb):
            if i:
                x += S.text(x, 61, "›", 10.5, "m") + 5
            last = i == len(crumb) - 1
            x += S.text(x, 61, part, 10.5, "t" if last else "selF", bold=last) + 5
        bottom = 68
    if right3:
        S.text(348, 61, right3, 9.5, "loopF", mono=True, anchor="end")
    tree(S)
    return bottom


def menu_popover(S, items):
    chrome(S)
    x, y, w = 206, 40, 138
    h = len(items) * 32 + 10
    rect(S, x + 1, y + 3, w, h, 10, "shadow")
    rect(S, x, y, w, h, 10, "bg")
    frame(S, x, y, w, h, 10, "lnS", 1)
    for i, (icon, lab, cls) in enumerate(items):
        yy = y + 5 + i * 32
        if i:
            S.raw(f'<path d="M{x+10} {yy}H{x+w-10}" class="ln"/>')
        S.text(x + 14, yy + 21, icon, 12, cls, maxx=x + w)
        S.text(x + 34, yy + 21, lab, 11.5, cls, maxx=x + w - 6)
    tree(S)


# ------------------------------------------------------------ wait card (W-C2)
REVIEW_WAIT = dict(step="Review", where="Verify ▸ · pass 2", wait="12m",
                   q="Approve the export change?", ctx="Test pass 2 · 43 passed · +628 −58",
                   acts=["Approve", "Change", "Design wrong"])
ANSWER_WAIT = dict(step="Answer", where="Question round ▸ · pass 3", wait="6m",
                   q="Start a run from the UI in v1?", ctx=None, acts=None)


def wait_card(S, x, y, w, d, head=None, open_run=False):
    """The wait card: the step, the question, the wait time, the act buttons.
    head: optional line above (the run, for the session page and the inbox)."""
    h = 0
    hy = y
    if head:
        h += 18
    h += 96 if d["acts"] else 92
    if d["ctx"]:
        h += 0
    if open_run:
        h += 22
    rect(S, x, y, w, h, 10, "nowSoft")
    frame(S, x, y, w, h, 10, "nowS", 1.3)
    if head:
        S.text(x + 12, y + 15, head, 9.5, "m", mono=True, maxx=x + w - 10)
        hy = y + 16
    S.raw(f'<circle cx="{x+15}" cy="{hy+14}" r="4.5" class="human"/>')
    ww = S.text(x + 26, hy + 18, d["step"], 11.5, "humanT", bold=True)
    S.text(x + 26 + ww + 6, hy + 18, d["where"], 9.5, "m", mono=True, maxx=x + w - 50)
    S.text(x + w - 10, hy + 18, d["wait"], 10.5, "humanT", mono=True, bold=True, anchor="end", maxx=x + w)
    S.text(x + 12, hy + 39, d["q"], 13, "t", bold=True, maxx=x + w - 10)
    if d["ctx"]:
        S.text(x + 12, hy + 55, d["ctx"], 9.5, "m", mono=True, maxx=x + w - 10)
    by = hy + 62
    if d["acts"]:
        buttons(S, x + 10, by, w - 20, d["acts"], h=30, fs=11)
    else:
        fy = hy + 50
        rect(S, x + 10, fy, w - 96, 30, 7, "bg")
        frame(S, x + 10, fy, w - 96, 30, 7, "lnS", 1.2)
        S.text(x + 20, fy + 19, "Type the answer…", 11, "m", maxx=x + w - 90)
        rect(S, x + w - 78, fy, 68, 30, 7, "nowF")
        S.text(x + w - 44, fy + 19.5, "Answer", 11.5, "bg", bold=True, anchor="middle", maxx=x + w)
    if open_run:
        link(S, x + 12, y + h - 9, "Open run", 10.5, x + w - 10)
    return y + h


# ------------------------------------------------------------ next bar
def next_bar(S, y, ways):
    """One small line: NEXT, then way → target pairs."""
    chrome(S)
    h = 22
    rect(S, 12, y, 336, h, 8, "bg")
    frame(S, 12, y, 336, h, 8, "nowS", 1.1, "4 3")
    x = 20
    x += S.text(x, y + 15, "NEXT", 9, "nowF", mono=True, bold=True) + 8
    for i, (way, tgt) in enumerate(ways):
        if i:
            x += S.text(x, y + 15, "·", 10, "m") + 5
        x += S.text(x, y + 15, way, 9.5, "t", bold=True) + 4
        x += S.text(x, y + 15, "→", 9.5, "nowF", mono=True) + 4
        x += S.text(x, y + 15, tgt, 9.5, "t", maxx=344) + 5
    tree(S)
    return y + h


def legend(S, y, items):
    chrome(S)
    x = 12
    for kind, lab in items:
        cy = y - 3.5
        if kind in ("agent", "human", "tool"):
            S.raw(f'<circle cx="{x+4}" cy="{cy}" r="4" class="{kind}"/>')
        elif kind == "child":
            S.raw(f'<rect x="{x-0.5}" y="{cy-4.5}" width="9" height="9" rx="2" fill="none" class="childS" stroke-width="1.4" stroke-dasharray="2.5 1.5"/>')
        elif kind == "exit":
            S.raw(f'<circle cx="{x+4}" cy="{cy}" r="4" class="soft"/><circle cx="{x+4}" cy="{cy}" r="4" fill="none" class="exitS" stroke-width="1.4"/>')
        elif kind == "restart":
            S.raw(f'<circle cx="{x+4}" cy="{cy}" r="4" fill="none" class="loopS" stroke-width="1.4" stroke-dasharray="2 1.5"/>')
        w = S.text(x + 12, y, lab, 9.5, "m")
        x += 12 + w + 9
    if x > 356:
        S.issues.append(f"legend too wide: {x}")
    tree(S)


LEG = [("agent", "agent task"), ("tool", "quick op"), ("human", "you"), ("child", "workflow ▸"),
       ("exit", "exit"), ("restart", "restart ↺")]


# ------------------------------------------------------------ nodes and rows
def node(S, x, y, kind, state, r=7):
    if kind == "child":
        s = r + 0.5
        if state == "now":
            S.raw(f'<circle cx="{x}" cy="{y}" r="{r+7}" class="nowSoft"/>')
            rect(S, x - s - 3, y - s - 3, 2 * s + 6, 2 * s + 6, 5, "bg")
            frame(S, x - s - 3, y - s - 3, 2 * s + 6, 2 * s + 6, 5, "nowS", 3)
        else:
            rect(S, x - s, y - s, 2 * s, 2 * s, 3, "bg")
        rect(S, x - s + 1, y - s + 1, 2 * s - 2, 2 * s - 2, 3, "childSoft")
        frame(S, x - s + 1, y - s + 1, 2 * s - 2, 2 * s - 2, 3, "childS", 1.6, "3 2")
        S.raw(f'<path d="M{x-2} {y-3.5}L{x+2.5} {y}L{x-2} {y+3.5}Z" class="childF"/>')
        return
    if kind in ("exit", "restart"):
        g = ' opacity=".5"' if state == "fut" else ""
        S.raw(f'<g{g}>')
        S.raw(f'<circle cx="{x}" cy="{y}" r="{r+0.5}" class="bg"/>')
        if kind == "exit":
            S.raw(f'<circle cx="{x}" cy="{y}" r="{r-0.5}" class="soft"/>')
            S.raw(f'<circle cx="{x}" cy="{y}" r="{r-0.5}" fill="none" class="exitS" stroke-width="1.8"/>')
            S.raw(f'<circle cx="{x}" cy="{y}" r="2" class="exitF"/>')
        else:
            S.raw(f'<circle cx="{x}" cy="{y}" r="{r-0.5}" fill="none" class="loopS" stroke-width="1.6" stroke-dasharray="2.6 2"/>')
        if state == "next":
            S.raw(f'<circle cx="{x}" cy="{y}" r="{r+3}" fill="none" class="nowS" stroke-width="1.4" stroke-dasharray="3 2"/>')
        S.raw('</g>')
        return
    if state == "now":
        S.raw(f'<circle cx="{x}" cy="{y}" r="{r+6}" class="nowSoft"/>')
        S.raw(f'<circle cx="{x}" cy="{y}" r="{r+1.5}" class="bg"/>')
        S.raw(f'<circle cx="{x}" cy="{y}" r="{r+1.5}" fill="none" class="nowS" stroke-width="3"/>')
        S.raw(f'<circle cx="{x}" cy="{y}" r="{r-2.5}" class="{kind}"/>')
    elif state == "next":
        S.raw(f'<circle cx="{x}" cy="{y}" r="{r+1}" class="bg"/>')
        S.raw(f'<circle cx="{x}" cy="{y}" r="{r+1}" fill="none" class="nowS" stroke-width="1.6" stroke-dasharray="3 2"/>')
        S.raw(f'<circle cx="{x}" cy="{y}" r="{r-3}" class="{kind}" opacity=".7"/>')
    elif state == "fut":
        S.raw(f'<circle cx="{x}" cy="{y}" r="{r}" class="bg"/>')
        S.raw(f'<g opacity=".5"><circle cx="{x}" cy="{y}" r="{r-0.5}" fill="none" class="futS" stroke-width="1.3"/>'
              f'<circle cx="{x}" cy="{y}" r="{r-3.5}" class="{kind}"/></g>')
    else:
        ring = "passS" if state == "pass" else "failS"
        S.raw(f'<circle cx="{x}" cy="{y}" r="{r+0.5}" class="bg"/>')
        S.raw(f'<circle cx="{x}" cy="{y}" r="{r-0.5}" fill="none" class="{ring}" stroke-width="1.6"/>')
        S.raw(f'<circle cx="{x}" cy="{y}" r="{r-3.5}" class="{kind}"/>')


def fold_node(S, x, y, r=7, h=24):
    rect(S, x - r, y - h / 2, 2 * r, h, r, "bg")
    frame(S, x - r + 0.8, y - h / 2 + 0.8, 2 * r - 1.6, h - 1.6, r - 0.8, "passS", 1.6)
    for dy in (-5, 0, 5):
        S.raw(f'<circle cx="{x}" cy="{y+dy}" r="1.6" class="m"/>')


def path(x1, y1, x2, y2, h=14):
    s = 1 if y2 > y1 else -1
    if x1 == x2:
        return f"M{x1} {y1}V{y2}"
    if x2 > x1:
        return f"M{x1} {y1}C{x1} {y1+s*h*0.6:.1f} {x2} {y1+s*h*0.4:.1f} {x2} {y1+s*h}V{y2}"
    return f"M{x1} {y1}V{y2-s*h}C{x1} {y2-s*h*0.4:.1f} {x2} {y2-s*h*0.6:.1f} {x2} {y2}"


def pill(S, x, y, label, sc, tc, fs=9.5, maxx=352, h=17):
    w = tw(label, fs, mono=True) + 14
    rect(S, x, y - h / 2, w, h, h / 2, "bg")
    frame(S, x, y - h / 2, w, h, h / 2, sc, 1.2)
    S.text(x + 7, y + fs * 0.35, label, fs, tc, mono=True, maxx=maxx)
    return w


ICON = {"pass": ("✓", "passF"), "fail": ("✕", "failF"), "now": ("NOW", "nowF")}
G = dict(rowH=30, labelH=24, foldH=32, TX=92, RX=346, r=7)
M, P1, P2 = 26, 46, 66


def row_h(r):
    return {"n": G["rowH"], "f": G["foldH"]}.get(r["k"], G["labelH"])


def layout(rows, y0):
    y = y0
    pos = {}
    for r in rows:
        h = row_h(r)
        r["top"], r["bot"], r["y"] = y, y + h, y + h / 2
        pos[r["id"]] = r
        y += h
    return pos, y


def draw_graph(S, rows, edges, y0):
    pos, yend = layout(rows, y0)
    for r_ in rows:
        if r_["k"] == "n" and r_.get("st") == "now":
            rect(S, 8, r_["top"] + 2, G["RX"] - 2, r_["bot"] - r_["top"] - 4, 8, "nowSoft", "", ) if False else \
                S.raw(f'<rect x="8" y="{r_["top"]+2:.1f}" width="{G["RX"]-2}" height="{r_["bot"]-r_["top"]-4:.1f}" rx="8" class="nowSoft"/>', "under")
            # the marker on the current step: a bar on the left edge
            S.raw(f'<rect x="2" y="{r_["top"]+5:.1f}" width="4" height="{r_["bot"]-r_["top"]-10:.1f}" rx="2" class="nowF"/>', "under")
    for a, b, cls in edges:
        A, B = pos[a], pos[b]
        S.raw(f'<path d="{path(A["x"], A["y"], B["x"], B["y"])}" class="{cls}"/>', "edges")
    for r_ in rows:
        vis = r_["bot"] > S.vis[0] + 2 and r_["top"] < S.vis[1] - 2
        save = S.issues
        if not vis:
            S.issues = []
        draw_row(S, r_)
        if not vis:
            S.issues = save
    return pos, yend


def draw_row(S, r_):
    TX, RX = G["TX"], G["RX"]
    y = r_["y"]
    k = r_["k"]
    if k == "n":
        node(S, r_["x"], y, r_["kind"], r_["st"], G["r"])
        draw_text(S, r_)
    elif k == "f":
        fold_node(S, r_["x"], y, G["r"])
        w = S.text(TX, y + 4, r_["t"], 12, "t", bold=True)
        S.text(TX + w + 8, y + 4, r_["d"], 9.5, "m", mono=True, maxx=RX - 16)
        S.text(RX, y + 4, "▾", 10, "m", anchor="end")
    elif k == "loop":
        pill(S, TX, y, r_["t"], "loopS", "loopF", maxx=RX)
    elif k == "split":
        pill(S, TX, y, r_["t"], "futS", "m", maxx=RX)


def draw_text(S, r_):
    TX, RX = G["TX"], G["RX"]
    y = r_["y"] + 4
    st = r_["st"]
    fade = ' opacity=".55"' if st == "fut" else ""
    S.cur.append(f"<g{fade}>")
    ic = ICON.get(st)
    rw = tw(ic[0], 10, mono=True) + 6 if ic else 0
    tcls = "m" if r_["kind"] in ("exit", "restart") else "t"
    x = TX + S.text(TX, y, r_["t"], 12, tcls, bold=True, maxx=RX - rw) + 7
    if r_.get("chip"):
        x += chip(S, x, y, r_["chip"], "loopS", "loopF") + 6
    if r_.get("you"):
        x += you_pill(S, x, y) + 6
    if r_.get("d"):
        S.text(x, y, r_["d"], 9.5, "m", mono=True, maxx=RX - rw)
    if ic:
        S.text(RX, y, ic[0], 10, ic[1], mono=True, bold=st == "now", anchor="end")
    S.cur.append("</g>")


def N(id, x, t, kind, st, d=None, **kw):
    r = dict(k="n", id=id, x=x, t=t, kind=kind, st=st, d=d)
    r.update(kw)
    return r


def L(id, k, t, x=0):
    return dict(k=k, id=id, t=t, x=x)


def F(id, t, d, x=M):
    return dict(k="f", id=id, t=t, d=d, x=x)


def topline(S, y):
    chrome(S)
    S.raw(f'<path d="M0 {y-0.5}H360" class="ln"/>')
    tree(S)


# ------------------------------------------------------------ W4 level 1
def l1_rows():
    return [
        N("spec", M, "Write spec", "agent", "pass", "spec.md"),
        N("split", M, "Split tasks", "agent", "pass", "3 tasks"),
        L("lsplit", "split", "split · parallel"),
        N("api", M, "API task", "agent", "pass", "+210 −12"),
        N("ui", P1, "UI task", "agent", "pass", "+340 −40"),
        N("docs", P2, "Docs task", "agent", "pass", "+80"),
        N("integ", M, "Integrate", "tool", "pass", "join · merged"),
        N("verify", M, "Verify ▸", "child", "now", "Review", chip="↺ pass 2", you=True),
        N("release", M, "Release", "tool", "next", "approved"),
        N("done", M, "exit · done", "exit", "fut"),
        N("rs", P1, "↺ restart", "restart", "next", "design wrong"),
        N("esc", P2, "exit · Escalate", "exit", "next", "limit"),
    ]


L1_EDGES = [
    ("spec", "split", "edge"), ("split", "api", "edge"), ("split", "ui", "edge"), ("split", "docs", "edge"),
    ("api", "integ", "edge"), ("ui", "integ", "edge"), ("docs", "integ", "edge"),
    ("integ", "verify", "edge"),
    ("verify", "release", "enext"), ("release", "done", "efut"),
    ("verify", "rs", "enext"), ("verify", "esc", "enext"),
]
L1_NEXT = [("approved", "Release"), ("wrong", "↺"), ("limit", "Escalate")]


def l1_page(S, menu=False):
    top = header(S, "Feature delivery #14", "1h 23m", "export-button", menu=menu)
    chrome(S)
    end = wait_card(S, 12, top + 6, 336, REVIEW_WAIT)
    end = next_bar(S, end + 6, L1_NEXT)
    S.clip_top = end + 6
    S.vis = (S.clip_top, 720)
    topline(S, S.clip_top)
    return S.clip_top


def run_a():
    S = SVG()
    y0 = l1_page(S, menu=True)
    draw_graph(S, l1_rows(), L1_EDGES, y0 + 4)
    legend(S, 708, LEG)
    menu_popover(S, [("⇥", "Force next step", "t"), ("↺", "Restart this flow", "t"), ("■", "Stop the run", "failF")])
    return S


# ------------------------------------------------------------ W4 level 2 Verify
def l2_rows(open_pass1=False):
    if open_pass1:
        head = [L("p1", "split", "▴ pass 1 · open"),
                N("test1", M, "Test", "tool", "fail", "41 passed · 2 failed"),
                N("fix1", P1, "Fix", "agent", "pass", "2 tests fixed")]
        first = "test1"
    else:
        head = [F("p1", "pass 1", "Test ✕ → Fix ✓")]
        first = "p1"
    rows = head + [
        L("r2", "loop", "↺ pass 2 · error · 2 tests failed"),
        N("test2", M, "Test", "tool", "pass", "43 passed"),
        N("review", M, "Review", "human", "now", "Test pass 2", you=True),
        N("xa", M, "exit · approved", "exit", "next", "approve"),
        N("fix2", P1, "Fix", "agent", "next", "change"),
        N("rs3", P1, "↺ pass 3", "restart", "fut"),
        N("xd", P2, "exit · design wrong", "exit", "next"),
    ]
    edges = [(("fix1" if open_pass1 else "p1"), "test2", "loop"), ("test2", "review", "edge"),
             ("review", "xa", "enext"), ("review", "fix2", "enext"), ("fix2", "rs3", "efut"),
             ("review", "xd", "enext")]
    if open_pass1:
        edges.append(("test1", "fix1", "edge"))
    return rows, edges


def l2_page(S, open_pass1=False):
    top = header(S, "Feature delivery #14", "1h 23m", "export-button",
                 crumb=["Feature delivery", "Verify ▸"], right3="pass 2")
    chrome(S)
    end = wait_card(S, 12, top + 6, 336, REVIEW_WAIT)
    S.clip_top = end + 6
    S.vis = (S.clip_top, 720)
    topline(S, S.clip_top)
    rows, edges = l2_rows(open_pass1)
    pos, _ = draw_graph(S, rows, edges, S.clip_top + 4)
    legend(S, 708, LEG)
    return rows, pos


def run_b():
    S = SVG()
    l2_page(S)
    return S


# ------------------------------------------------------------ W3 level 2 Question round
def run_c():
    S = SVG()
    top = header(S, "Planning #7", "38m", "plan-runner-v2",
                 crumb=["Planning", "Question round ▸"], right3="pass 3")
    chrome(S)
    end = wait_card(S, 12, top + 6, 336, ANSWER_WAIT)
    end = next_bar(S, end + 6, [("answer", "Record"), ("then", "↺ pass 4 or exit clear")])
    S.clip_top = end + 6
    S.vis = (S.clip_top, 720)
    topline(S, S.clip_top)
    rows = [
        F("q1", "pass 1", "Q1 · From a session"),
        L("r2", "loop", "↺ pass 2 · expected · more"),
        F("q2", "pass 2", "Q2 · 3, then ask"),
        L("r3", "loop", "↺ pass 3 · expected · more"),
        N("ask3", M, "Ask", "agent", "pass", "Q3 · UI start in v1?"),
        N("ans3", M, "Answer", "human", "now", you=True),
        N("rec3", M, "Record", "agent", "next"),
        N("rs4", M, "↺ pass 4", "restart", "fut", "more"),
        N("xc", P1, "exit · clear", "exit", "fut", "done"),
    ]
    edges = [("q1", "q2", "loop"), ("q2", "ask3", "loop"), ("ask3", "ans3", "edge"),
             ("ans3", "rec3", "enext"), ("rec3", "rs4", "efut"), ("rec3", "xc", "efut")]
    draw_graph(S, rows, edges, S.clip_top + 4)
    legend(S, 708, LEG)
    return S


# ------------------------------------------------------------ W-5 sheets
def sheet(S, y):
    rect(S, 0, y - 4, 360, 724 - y, 18, "shadow")
    rect(S, 0, y, 360, 740 - y, 18, "soft")
    rect(S, 162, y + 8, 36, 4, 2, "m", ' opacity=".5"')


def dim(S):
    S.raw(f'<rect x="0" y="0" width="360" height="720" class="dim"/>')


def select_row(S, r_):
    x0, x1 = G["TX"] - 10, G["RX"] + 4
    h = r_["bot"] - r_["top"] - 2
    for cls in ("bg", "selSoft"):
        rect(S, x0, r_["top"] + 1, x1 - x0, h, 8, cls)
    frame(S, x0, r_["top"] + 1, x1 - x0, h, 8, "selS", 2)
    if r_["k"] == "n":
        node(S, r_["x"], r_["y"], r_["kind"], r_["st"], G["r"])
        S.raw(f'<circle cx="{r_["x"]}" cy="{r_["y"]}" r="{G["r"]+5}" fill="none" class="selS" stroke-width="2.5"/>')
        draw_text(S, r_)
    else:
        draw_row(S, r_)


def kv(S, y, k, v, vcls="t", mono=False, fs=10.5):
    S.text(16, y, k, 9.5, "m", mono=True)
    return S.text(84, y, v, fs, vcls, mono=mono, maxx=344)


def kv_link(S, y, k, v):
    S.text(16, y, k, 9.5, "m", mono=True)
    link(S, 84, y, v, 10.5)


def section(S, y, label):
    S.text(16, y, label, 9, "m", mono=True, bold=True)


def title_line(S, y, title, chipl, chipcls, right):
    w = S.text(16, y + 40, title, 17, "t", bold=True)
    sc, tc = {"loop": ("loopS", "loopF"), "now": ("nowS", "nowF")}[chipcls]
    cw = tw(chipl, 9.5, mono=True) + 12
    frame(S, 16 + w + 8, y + 28, cw, 16, 8, sc, 1.2)
    S.text(16 + w + 14, y + 40, chipl, 9.5, tc, mono=True)
    S.text(344, y + 40, right, 10, "m", mono=True, anchor="end")


def meter(S, x, y, used, total, cls="loopF"):
    for i in range(total):
        c = cls if i < used else "lnF"
        rect(S, x + i * 14, y - 7, 11, 6, 2, c)
    return total * 14


def sheet_a():
    S = SVG()
    rows, pos = l2_page(S, open_pass1=True)
    chrome(S)
    dim(S)
    select_row(S, pos["test1"])
    y = 400
    assert pos["test1"]["bot"] < y - 8
    sheet(S, y)
    title_line(S, y, "Test", "pass 1", "loop", "Verify ▸ · step 1")
    kv(S, y + 68, "action", "quick operation")
    kv(S, y + 88, "state", "done · ✕ failed", "failF")
    kv(S, y + 108, "time", "3m · 10:42 → 10:45", mono=True, fs=10)
    kv(S, y + 128, "result", "41 passed · 2 failed", mono=True, fs=10)
    hline(S, 16, 344, y + 142.5)
    section(S, y + 162, "DOCUMENTS OF THIS STEP RUN")
    kv_link(S, y + 182, "input", "integrate-report.md")
    kv_link(S, y + 202, "output", "test-report.md")
    hline(S, 16, 344, y + 216.5)
    link(S, 16, y + 240, "Step definition · Test", 11.5)
    S.text(16, y + 262, "Went to Fix, then ↺ pass 2 (error).", 10, "m", maxx=344)
    assert y + 262 <= 712
    tree(S)
    return S


def sheet_b():
    S = SVG()
    rows, pos = l2_page(S)
    chrome(S)
    dim(S)
    select_row(S, pos["review"])
    y = pos["review"]["bot"] + 22
    sheet(S, y)
    title_line(S, y, "Review", "pass 2", "now", "Verify ▸ · step 2")
    kv(S, y + 68, "action", "agent task · you decide")
    kv(S, y + 88, "state", "now · waits for you 12m", "humanT")
    kv(S, y + 108, "time", "started 10:58", mono=True, fs=10)
    hline(S, 16, 344, y + 122.5)
    section(S, y + 142, "DOCUMENTS OF THIS STEP RUN")
    kv_link(S, y + 162, "input", "test-report.md (pass 2)")
    kv_link(S, y + 182, "input", "diff feat/export +628 −58")
    kv(S, y + 202, "output", "review.md · after you act", "m")
    hline(S, 16, 344, y + 216.5)
    section(S, y + 236, "YOUR CHOICE")
    buttons(S, 16, y + 246, 328, ["Approve", "Change", "Design wrong"], h=34, fs=11.5)
    link(S, 16, y + 306, "Step definition · Review", 11.5)
    assert y + 306 <= 712, y + 306
    tree(S)
    return S


def sheet_c():
    S = SVG()
    rows, pos = l2_page(S)
    chrome(S)
    dim(S)
    r2 = pos["r2"]
    TX = G["TX"]
    w = tw(r2["t"], 9.5, mono=True) + 14
    rect(S, TX - 4, r2["y"] - 12.5, w + 8, 25, 12.5, "bg")
    pill(S, TX, r2["y"], r2["t"], "loopS", "loopF")
    frame(S, TX - 4, r2["y"] - 12.5, w + 8, 25, 12.5, "selS", 2)
    y = r2["bot"] + 26
    sheet(S, y)
    title_line(S, y, "↺ pass 2", "restart", "loop", "Verify ▸")
    kv(S, y + 68, "reason", "error · Test failed", "failF")
    kv(S, y + 88, "ended by", "Fix · pass 1")
    S.text(16, y + 108, "count", 9.5, "m", mono=True)
    mw = meter(S, 84, y + 108, 1, 3)
    S.text(84 + mw + 4, y + 108, "1 of 3 (error)", 10, "loopF", mono=True)
    kv(S, y + 128, "at limit", "exit limit → Escalate")
    hline(S, 16, 344, y + 142.5)
    section(S, y + 162, "STEP 1 (TEST) SEES IN PASS 2")
    by = y + 170
    lines = [("last pass ended: ↺ error", "t"),
             ("Test ✕ 41 passed · 2 failed", "failF"),
             ("Fix  ✓ 2 tests fixed", "passF")]
    rect(S, 16, by, 328, 14 + 16 * len(lines), 8, "bg")
    for i, (s, c) in enumerate(lines):
        S.text(28, by + 20 + 16 * i, s, 10, c, mono=True, maxx=336)
    by += 14 + 16 * len(lines)
    link(S, 16, by + 28, "Restart settings · Verify", 11.5)
    assert by + 28 <= 712, by + 28
    tree(S)
    return S


# ------------------------------------------------------------ W-C1 run row
def page_title(S, title, sub=None, back=True):
    chrome(S)
    x = 16
    if back:
        S.raw('<path d="M22 18L15 26L22 34" class="edge" stroke-width="2"/>')
        x = 38
    S.text(x, 27, title, 15, "t", bold=True, maxx=300)
    if sub:
        S.text(x, 44, sub, 10, "m", maxx=348)
    tree(S)


def run_row(S, x, y, w, wf, path_, st, elapsed, you=False, soft=True):
    """W-C1: workflow name, level path to the current step, state dot, elapsed time, you marker."""
    h = 44
    if soft:
        rect(S, x, y, w, h, 9, "bg")
        frame(S, x, y, w, h, 9, "lnS", 1)
    dot = {"wait": "human", "run": "nowF", "done": "passF"}[st]
    S.raw(f'<circle cx="{x+14}" cy="{y+16}" r="4.5" class="{dot}"/>')
    S.text(x + 26, y + 20, wf, 12, "t", bold=True, maxx=x + w - 60)
    S.text(x + w - 22, y + 20, elapsed, 10, "m", mono=True, anchor="end", maxx=x + w)
    xx = x + 26
    xx += S.text(xx, y + 36, path_, 10, "t", mono=True, maxx=x + w - 60) + 6
    if you:
        you_pill(S, xx, y + 36)
    S.text(x + w - 10, y + 30, "›", 14, "m", anchor="end", maxx=x + w)
    return y + h


def session_head(S, name, goal):
    chrome(S)
    S.raw('<path d="M22 18L15 26L22 34" class="edge" stroke-width="2"/>')
    S.text(38, 27, name, 15, "t", bold=True)
    S.raw('<circle cx="42" cy="40.5" r="3.5" class="human"/>')
    S.text(50, 44, "waits for you", 10, "humanT", bold=True)
    S.text(344, 27, "S-14", 10, "m", mono=True, anchor="end")
    S.text(38, 60, goal, 10, "m", maxx=348)
    hline(S, 0, 360, 70.5)
    tree(S)


def bubble(S, y, who, lines, mine=False):
    h = 12 + 15 * len(lines)
    x, w = (96, 248) if mine else (16, 280)
    rect(S, x, y, w, h, 10, "selSoft" if mine else "soft")
    for i, l in enumerate(lines):
        S.text(x + 10, y + 17 + 15 * i, l, 10.5, "t", maxx=x + w - 6)
    S.text(x + (w - 4 if mine else 4), y - 4, who, 9, "m", mono=True, anchor="end" if mine else "start", maxx=352)
    return y + h


def composer(S):
    rect(S, 12, 668, 336, 40, 12, "soft")
    S.text(26, 692, "Message export-button…", 11, "m")


def row_a():
    S = SVG()
    session_head(S, "export-button", "Add an export button to the report page")
    section(S, 92, "RUN")
    end = run_row(S, 12, 100, 336, "Feature delivery #14", "Verify ▸ Review", "wait", "1h 23m", you=True)
    S.text(16, end + 16, "Tap the row: the run page opens.", 9.5, "m")
    hline(S, 0, 360, end + 28.5)
    y = end + 52
    y = bubble(S, y, "you", ["Add a CSV export to the report", "page. Start Feature delivery."], mine=True) + 22
    y = bubble(S, y, "export-button", ["Run #14 started. Spec written:", "spec.md (6 parts)."]) + 22
    y = bubble(S, y, "export-button", ["3 tasks done and merged.", "Verify ▸ runs now."]) + 22
    y = bubble(S, y, "export-button", ["Pass 1: 2 tests failed. Fix done.", "Pass 2: 43 passed.",
                                        "Review waits for you."]) + 10
    assert y < 660, y
    composer(S)
    return S


SESSIONS = [
    ("export-button", "Review waits for you", "12m", "human",
     ("Feature delivery #14", "Verify ▸ Review", "wait", "1h 23m", True)),
    ("login-timeout", "UI task runs", "2m", "nowF",
     ("Feature delivery #15", "UI task", "run", "34m", False)),
    ("store-migration", "Wrote store.md, 3 parts", "20m", "passF", None),
    ("tray-icon", "Build passed", "1h", "passF", None),
    ("docs-cleanup", "Removed 4 old pages", "2h", "passF", None),
    ("question-bot", "Idle", "1d", "futS", None),
]


def session_list(S, sessions, y, bar=None):
    for name, last, t, dotc, run in sessions:
        if dotc == "futS":
            S.raw(f'<circle cx="24" cy="{y+16}" r="4" fill="none" class="futS" stroke-width="1.5"/>')
        else:
            S.raw(f'<circle cx="24" cy="{y+16}" r="4.5" class="{dotc}"/>')
        S.text(36, y + 20, name, 12.5, "t", bold=True, maxx=300)
        S.text(344, y + 20, t, 10, "m", mono=True, anchor="end")
        S.text(36, y + 36, last, 10, "m", maxx=344)
        y += 46
        if run:
            if bar:
                y = bar(S, y, run) + 8
            else:
                y = run_row(S, 30, y - 2, 318, *run) + 8
        hline(S, 12, 348, y - 1.5)
        y += 6
    return y


def row_b():
    S = SVG()
    page_title(S, "Sessions", "6 sessions · 1 waits for you", back=False)
    hline(S, 0, 360, 56.5)
    y = session_list(S, SESSIONS, 66)
    assert y < 712, y
    return S


# ------------------------------------------------------------ step bar try
SEG_CLS = {"pass": "passF", "fail": "failF", "now": "nowF", "fut": "lnF", "wait": "human"}


def step_bar(S, x, y, w, segs, h=10, gap=3):
    """segs: list of dicts. kind: step | par | child | stub. Each has weight 'wt'.
    child: 'inner' = list of states, 'rs' = restart count, 'st'.
    par: 'lanes' = list of states. stub: 'rs' = folded level-1 passes."""
    tot = sum(s["wt"] for s in segs)
    avail = w - gap * (len(segs) - 1)
    xx = x
    for s in segs:
        sw = avail * s["wt"] / tot
        k = s["k"]
        if k == "step":
            if s["st"] == "fut":
                frame(S, xx + 0.5, y + 0.5, sw - 1, h - 1, 2.5, "futS", 1, "2 2")
            else:
                rect(S, xx, y, sw, h, 2.5, SEG_CLS[s["st"]])
        elif k == "par":
            n = len(s["lanes"])
            lh = (h - (n - 1)) / n
            for i, st in enumerate(s["lanes"]):
                if st == "fut":
                    rect(S, xx, y + i * (lh + 1), sw, lh, 1, "lnF")
                else:
                    rect(S, xx, y + i * (lh + 1), sw, lh, 1, SEG_CLS[st])
        elif k == "stub":
            rect(S, xx, y, sw, h, 2.5, "soft")
            frame(S, xx + 0.5, y + 0.5, sw - 1, h - 1, 2.5, "loopS", 1.1, "2 1.5")
        elif k == "child":
            pad = 2
            rect(S, xx - 0.5, y - 3, sw + 1, h + 6, 3.5, "childSoft")
            frame(S, xx - 0.5, y - 3, sw + 1, h + 6, 3.5, "nowS" if s["st"] == "now" else "childS", 1.4, "3 2")
            inner = s["inner"]
            ig = 1.5
            iw = (sw - 2 * pad - ig * (len(inner) - 1)) / len(inner)
            ix = xx + pad
            for st in inner:
                rect(S, ix, y + 1.5, iw, h - 3, 1.5, SEG_CLS[st] if st != "fut" else "lnF")
                ix += iw + ig
        s["_x"], s["_w"] = xx, sw
        xx += sw + gap
    return segs


def bar_labels(S, segs, y, fs=9):
    for s in segs:
        if s.get("lab"):
            cls = s.get("labc", "m")
            S.text(s["_x"] + s["_w"] / 2, y, s["lab"], fs, cls, mono=True, anchor="middle", bold=cls == "nowF")


W4_BAR = [
    dict(k="step", st="pass", wt=1), dict(k="step", st="pass", wt=1),
    dict(k="par", lanes=["pass", "pass", "pass"], wt=1.4), dict(k="step", st="pass", wt=1),
    dict(k="child", st="now", inner=["pass", "wait"], rs=1, wt=3.2, lab="↺1 · Review", labc="nowF"),
    dict(k="step", st="fut", wt=1),
]
W3_BAR = [
    dict(k="step", st="pass", wt=1),
    dict(k="child", st="now", inner=["pass", "wait", "fut"], rs=2, wt=3.4, lab="↺2 · Answer", labc="nowF"),
    dict(k="step", st="fut", wt=1), dict(k="step", st="fut", wt=1), dict(k="step", st="fut", wt=1),
]
W15_BAR = [
    dict(k="step", st="pass", wt=1), dict(k="step", st="pass", wt=1),
    dict(k="par", lanes=["pass", "now", "pass"], wt=1.4, lab="UI", labc="nowF"), dict(k="step", st="fut", wt=1),
    dict(k="child", st="fut", inner=["fut", "fut"], rs=0, wt=2.2), dict(k="step", st="fut", wt=1),
]
W12_BAR = [
    dict(k="stub", wt=0.9, lab="↺2", labc="loopF"),
    dict(k="step", st="pass", wt=1),
    dict(k="child", st="now", inner=["pass", "fail", "pass", "now"], rs=1, wt=3.4, lab="↺1 · Verify", labc="nowF"),
    dict(k="step", st="fut", wt=1),
]


def bar_row(S, y, run):
    wf, path_, st, elapsed, you, segs = run
    x, w = 36, 308
    dot = {"wait": "human", "run": "nowF"}[st]
    S.raw(f'<circle cx="{x+4}" cy="{y+4}" r="3.5" class="{dot}"/>')
    xx = x + 12
    xx += S.text(xx, y + 8, wf, 10.5, "t", bold=True) + 6
    if you:
        you_pill(S, xx, y + 8)
    S.text(x + w, y + 8, elapsed, 9.5, "m", mono=True, anchor="end")
    segs = [dict(s) for s in segs]
    step_bar(S, x, y + 18, w, segs)
    bar_labels(S, segs, y + 44)
    return y + 48


def row_c():
    S = SVG()
    page_title(S, "Sessions", "Try: a step bar in the run row", back=False)
    hline(S, 0, 360, 56.5)
    sess = [
        ("export-button", "Review waits for you", "12m", "human",
         ("Feature delivery #14", "", "wait", "1h 23m", True, W4_BAR)),
        ("plan-runner-v2", "Answer waits for you", "6m", "human",
         ("Planning #7", "", "wait", "38m", True, W3_BAR)),
        ("api-500s", "Mitigate runs, pass 3", "4m", "nowF",
         ("Incident triage #12", "", "run", "1h 10m", False, W12_BAR)),
    ]
    y = session_list(S, sess, 64, bar=bar_row)
    # how to read the bar: one big bar with notes
    y += 4
    section(S, y + 10, "HOW TO READ THE BAR (A TRY)")
    rect(S, 12, y + 18, 336, 214, 10, "soft")
    segs = [dict(s) for s in W4_BAR]
    segs[4]["lab"] = None
    by = y + 64
    step_bar(S, 24, by, 312, segs, h=16, gap=4)
    # notes above the bar
    notes_top = [(0, "Write spec", 0), (2, "parallel", 0), (4, "Verify ▸ · child level", 1)]
    for idx, lab, lvl in notes_top:
        s = segs[idx]
        cx = s["_x"] + (s["_w"] / 2 if idx != 4 else 10)
        ty = by - 12 - 12 * lvl if idx != 4 else by - 14
        S.raw(f'<path d="M{cx:.1f} {ty+3}V{by-4}" class="ln" stroke-width="1"/>')
        S.text(cx - (0 if idx == 4 else 0), ty, lab, 9, "t" if idx != 4 else "childF", mono=True,
               anchor="start" if idx == 4 else "middle", bold=idx == 4)
    # notes under the inner segments of the child
    s = segs[4]
    iw = (s["_w"] - 4 - 1.5) / 2
    S.text(s["_x"] + 2 + iw / 2, by + 30, "Test", 9, "m", mono=True, anchor="middle")
    S.text(s["_x"] + 2 + iw * 1.5 + 1.5, by + 30, "Review", 9, "humanT", mono=True, anchor="middle", bold=True)
    S.text(s["_x"] + s["_w"] / 2, by + 43, "pass 2 · ↺1", 9, "loopF", mono=True, anchor="middle")
    last = segs[5]
    S.text(last["_x"] + last["_w"], by + 30, "Release", 9, "m", mono=True, anchor="end")
    ny = by + 62
    for i, line in enumerate([
            "One segment for each level-1 step.",
            "Parallel branches stack in one segment.",
            "A child flow ▸ holds its own level inside.",
            "↺N: restarts in that level. Only the last pass shows.",
            "A ↺ box at the start: old level-1 passes."]):
        S.text(24, ny + 15 * i, line, 10, "t", maxx=344)
    assert ny + 15 * 4 < 712, ny
    # the stub example
    return S


# ------------------------------------------------------------ W-C2 wait card
def wait_a():
    S = SVG()
    top = header(S, "Feature delivery #14", "1h 23m", "export-button")
    chrome(S)
    end = wait_card(S, 12, top + 6, 336, REVIEW_WAIT)
    # callouts for the parts of the card
    cy = top + 6
    notes = [(cy + 24, "1 the step and where it is"), (cy + 24, None),
             (cy + 45, "2 the question"), (cy + 61, "3 what it is about"),
             (cy + 77, "4 the act buttons = ways out")]
    end2 = next_bar(S, end + 6, L1_NEXT)
    S.clip_top = end2 + 6
    S.vis = (S.clip_top, 720)
    topline(S, S.clip_top)
    draw_graph(S, l1_rows(), L1_EDGES, S.clip_top + 4)
    chrome(S)
    # fade the tree to put the card in focus
    S.raw(f'<rect x="0" y="{S.clip_top}" width="360" height="{720-S.clip_top}" class="bg" opacity=".72"/>')
    py = 452
    rect(S, 12, py, 336, 252, 10, "soft")
    section(S, py + 20, "THE WAIT CARD ON THE RUN PAGE")
    lines = [
        ("1", "Review · Verify ▸ · pass 2", "the step and its level path"),
        ("2", "12m", "how long it waits"),
        ("3", "Approve the export change?", "the question"),
        ("4", "Test pass 2 · 43 passed", "short context"),
        ("5", "Approve · Change · Design wrong", "the ways out of the step"),
    ]
    yy = py + 44
    for n, a, b in lines:
        S.raw(f'<circle cx="28" cy="{yy-4}" r="7" class="nowF"/>')
        S.text(28, yy - 0.5, n, 9.5, "bg", mono=True, bold=True, anchor="middle")
        S.text(42, yy, a, 10, "t", mono=True, maxx=344)
        S.text(42, yy + 14, b, 10, "m", maxx=344)
        yy += 34
    S.text(24, yy + 4, "A tap on the card head opens the level", 10, "t", maxx=344)
    S.text(24, yy + 19, "of the wait (Verify ▸), with the marker.", 10, "t", maxx=344)
    assert yy + 19 < 700, yy
    # numbers on the card
    for n, (nx, ny) in (("1", (8, top + 6 + 14)), ("2", (352, top + 6 + 14)), ("3", (8, top + 6 + 35)),
                        ("4", (8, top + 6 + 51)), ("5", (8, top + 6 + 77))):
        S.raw(f'<circle cx="{nx}" cy="{ny}" r="6.5" class="nowF"/>')
        S.text(nx, ny + 3.3, n, 9, "bg", mono=True, bold=True, anchor="middle", minx=0, maxx=360)
    tree(S)
    return S


def wait_b():
    S = SVG()
    session_head(S, "export-button", "Add an export button to the report page")
    section(S, 92, "RUN")
    end = run_row(S, 12, 100, 336, "Feature delivery #14", "Verify ▸ Review", "wait", "1h 23m", you=True)
    end = wait_card(S, 12, end + 8, 336, REVIEW_WAIT)
    S.text(16, end + 16, "The same card as on the run page.", 9.5, "m")
    hline(S, 0, 360, end + 28.5)
    y = end + 52
    y = bubble(S, y, "export-button", ["3 tasks done and merged.", "Verify ▸ runs now."]) + 22
    y = bubble(S, y, "export-button", ["Pass 1: 2 tests failed. Fix done.", "Pass 2: 43 passed.",
                                        "Review waits for you."]) + 10
    assert y < 660, y
    composer(S)
    return S


def wait_c():
    S = SVG()
    page_title(S, "Inbox", "3 questions wait for you", back=False)
    hline(S, 0, 360, 56.5)
    y = 66
    y = wait_card(S, 12, y, 336, REVIEW_WAIT, head="export-button · Feature delivery #14", open_run=True) + 10
    y = wait_card(S, 12, y, 336, ANSWER_WAIT, head="plan-runner-v2 · Planning #7", open_run=True) + 10
    plain = dict(step="Question", where="no run", wait="20m", q="SQLite or JSON files?",
                 ctx="store-migration needs a store format", acts=["SQLite", "JSON files"])
    y = wait_card(S, 12, y, 336, plain, head="store-migration") + 12
    S.text(16, y + 4, "A run wait is a normal question card.", 10, "m", maxx=344)
    S.text(16, y + 19, "It adds one link: Open run.", 10, "m", maxx=344)
    assert y + 19 < 712, y
    return S


def main():
    jobs = {"run-a.svg": run_a, "run-b.svg": run_b, "run-c.svg": run_c,
            "sheet-a.svg": sheet_a, "sheet-b.svg": sheet_b, "sheet-c.svg": sheet_c,
            "row-a.svg": row_a, "row-b.svg": row_b, "row-c.svg": row_c,
            "wait-a.svg": wait_a, "wait-b.svg": wait_b, "wait-c.svg": wait_c}
    bad = False
    for name, fn in jobs.items():
        S = fn()
        for i in S.issues:
            print(name, i)
            bad = True
        with open(os.path.join(OUT, name), "w", encoding="utf-8") as fh:
            fh.write(S.out())
    meta = {"rows": [
        {"id": "r5run", "name": "W-4 · Run",
         "why": "One screen for each level. A short header, the wait card, a small Next bar. The graph is on the left, one short row for each step run on the right, and a marker on the current step. Old passes fold into one row. A restart ↺ is a chip.",
         "sketches": [
             {"file": "run-a.svg", "caption": "W4 level 1 · wait card · ⋯ menu"},
             {"file": "run-b.svg", "caption": "W4 Verify ▸ · pass 1 folded"},
             {"file": "run-c.svg", "caption": "W3 Question round ▸ · pass 3"},
         ]},
        {"id": "r5sheet", "name": "W-5 · Step run sheet",
         "why": "A tap on a row or a chip opens a sheet over the dimmed run page. It shows the action, the state, the time and the result, links to the actual input and output documents, and a link to the step definition. A waiting step has the act buttons.",
         "sketches": [
             {"file": "sheet-a.svg", "caption": "Done step: Test, pass 1"},
             {"file": "sheet-b.svg", "caption": "Waiting step: Review · act"},
             {"file": "sheet-c.svg", "caption": "Restart chip: ↺ pass 2"},
         ]},
        {"id": "r5row", "name": "W-C1 · Run row",
         "why": "One line shows a run on the session views: the workflow, the current step with its level path, a state dot, the elapsed time and a you marker. A tap opens the run page. The step bar is a try for hubs, restarts and levels.",
         "sketches": [
             {"file": "row-a.svg", "caption": "On the session page"},
             {"file": "row-b.svg", "caption": "In the session list"},
             {"file": "row-c.svg", "caption": "Try: step bar in the run row"},
         ]},
        {"id": "r5wait", "name": "W-C2 · Wait card",
         "why": "The card shows what a waiting run needs from you: the step, the question, how long it waits and the act buttons. It shows on the run page and on the owner session page. In the inbox it is a normal question card with an Open run link.",
         "sketches": [
             {"file": "wait-a.svg", "caption": "On the run page · the parts"},
             {"file": "wait-b.svg", "caption": "On the owner session page"},
             {"file": "wait-c.svg", "caption": "In the inbox · Open run link"},
         ]},
    ]}
    for row in meta["rows"]:
        assert len(row["why"].split()) <= 50, (row["id"], len(row["why"].split()))
        assert len(row["id"]) <= 10
        for s in row["sketches"]:
            assert len(s["caption"]) <= 36, s
    with open(os.path.join(OUT, "r5-run.json"), "w", encoding="utf-8") as fh:
        json.dump(meta, fh, ensure_ascii=False, indent=2)
    print("bad" if bad else "ok")


main()
