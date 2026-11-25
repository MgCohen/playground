# Round 5, part A: W-1 gallery, W-2 workflow definition, W-2a step definition.
# Based on the round 4 generator r4def/gen.py (copied, then changed).
import json, os
from xml.sax.saxutils import escape

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "r5")
os.makedirs(OUT, exist_ok=True)

CSS = """
.bg{fill:#fdfdfc}.soft{fill:#f1eee9}.t{fill:#2a2825}.m{fill:#6b665f}
.ln{stroke:rgba(27,26,24,.14);fill:none}.lnF{fill:rgba(27,26,24,.14)}
.edge{stroke:#8d877e;fill:none;stroke-width:1.6}
.rst{stroke:#c2410c;fill:none;stroke-width:1.6;stroke-dasharray:4 3}
.loopF{fill:#c2410c}.loopS{stroke:#c2410c}
.agent{fill:#1f5fbf}.human{fill:#ff6a1a}.tool{fill:#6b665f}
.agentS{stroke:#1f5fbf}.humanS{stroke:#ff6a1a}.toolS{stroke:#6b665f}.humanT{fill:#c2410c}
.childS{stroke:#2d6a4f}.childF{fill:#2d6a4f}.childSoft{fill:rgba(45,106,79,.10)}
.exitS{stroke:#5d6660}.exitF{fill:#5d6660}
.nowS{stroke:#7c4dff}.nowF{fill:#7c4dff}.nowSoft{fill:rgba(124,77,255,.12)}
.selS{stroke:#1f5fbf}.selF{fill:#1f5fbf}.selSoft{fill:rgba(31,95,191,.10)}
.futS{stroke:#8d877e}
.dim{fill:rgba(27,26,24,.30)}
.sans{font-family:Inter,system-ui,sans-serif}.mono{font-family:ui-monospace,monospace}
.shadow{fill:rgba(27,26,24,.10)}
@media (prefers-color-scheme: dark){
.bg{fill:#262522}.soft{fill:#2e2c29}.t{fill:#ece9e4}.m{fill:#a39d94}
.ln{stroke:rgba(255,255,255,.12)}.lnF{fill:rgba(255,255,255,.12)}
.edge{stroke:#8a847b}.futS{stroke:#8a847b}
.rst{stroke:#ff8a4c}.loopF{fill:#ff8a4c}.loopS{stroke:#ff8a4c}
.agent{fill:#6ea0ff}.human{fill:#ff7a33}.tool{fill:#a39d94}
.agentS{stroke:#6ea0ff}.humanS{stroke:#ff7a33}.toolS{stroke:#a39d94}.humanT{fill:#ff7a33}
.childS{stroke:#6fcf97}.childF{fill:#6fcf97}.childSoft{fill:rgba(111,207,151,.12)}
.exitS{stroke:#9ba59e}.exitF{fill:#9ba59e}
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
        self.check = True
        self.end = 0

    def text(self, x, y, s, fs, cls, mono=False, bold=False, anchor="start", maxx=352, minx=8):
        w = tw(s, fs, mono, bold)
        x0 = x - w if anchor == "end" else (x - w / 2 if anchor == "middle" else x)
        if self.check and (x0 + w > maxx + 0.5 or x0 < minx - 0.5):
            self.issues.append(f"overflow: {s!r} x0={x0:.0f} w={w:.0f} max={maxx}")
        if fs < 9:
            self.issues.append(f"font too small: {s!r}")
        fam = "mono" if mono else "sans"
        a = f' text-anchor="{anchor}"' if anchor != "start" else ""
        b = ' font-weight="600"' if bold else ""
        self.cur.append(
            f'<text x="{x:.1f}" y="{y:.1f}" font-size="{fs}" class="{cls} {fam}"{a}{b}>{escape(s)}</text>')
        self.end = max(self.end, y)
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


# ------------------------------------------------------------ small parts
def back(S, y=26):
    S.raw(f'<path d="M22 {y-8}L15 {y}L22 {y+8}" class="edge" stroke-width="2"/>')


def chev(S, x, y, cls="futS"):
    S.raw(f'<path d="M{x-3} {y-5}L{x+2} {y}L{x-3} {y+5}" fill="none" class="{cls}" stroke-width="1.6"/>')


def doc_icon(S, x, y):
    """A small document icon; (x, y) is the top left."""
    S.raw(f'<path d="M{x} {y}H{x+7}L{x+10} {y+3}V{y+13}H{x}Z" fill="none" class="futS" stroke-width="1.3"/>'
          f'<path d="M{x+7} {y}V{y+3}H{x+10}" fill="none" class="futS" stroke-width="1.1"/>')


def chip(S, x, y, label, sc, tc, fs=9.5, dash=False):
    w = tw(label, fs, mono=True) + 12
    da = ' stroke-dasharray="3 2"' if dash else ""
    S.raw(f'<rect x="{x:.1f}" y="{y-12:.1f}" width="{w:.1f}" height="16" rx="8" fill="none" class="{sc}" stroke-width="1.2"{da}/>')
    S.text(x + 6, y, label, fs, tc, mono=True)
    return w


def section(S, y, label):
    S.text(16, y, label, 9, "m", mono=True, bold=True)


def button(S, x, y, w, label, primary, h=38):
    if primary:
        S.raw(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="10" class="nowF"/>')
        S.text(x + w / 2, y + h / 2 + 4.5, label, 12, "bg", bold=True, anchor="middle", maxx=x + w)
    else:
        S.raw(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="10" class="bg"/>')
        S.raw(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="10" fill="none" class="nowS" stroke-width="1.4"/>')
        S.text(x + w / 2, y + h / 2 + 4.5, label, 12, "nowF", bold=True, anchor="middle", maxx=x + w)


def card(S, x, y, w, h, border=True):
    S.raw(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8" class="bg"/>')
    if border:
        S.raw(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8" fill="none" class="ln" stroke-width="1"/>')


ACT = {"agent": ("agent task", "agent"), "op": ("quick operation", "tool"), "wf": ("start workflow ▸", None)}


def act_icon(S, x, y, kind, r=4.5):
    if kind == "wf":
        S.raw(f'<rect x="{x-r}" y="{y-r}" width="{2*r}" height="{2*r}" rx="2.5" class="childSoft"/>'
              f'<rect x="{x-r}" y="{y-r}" width="{2*r}" height="{2*r}" rx="2.5" fill="none" class="childS" stroke-width="1.5" stroke-dasharray="2.5 1.5"/>')
    else:
        e = ACT[kind][1]
        S.raw(f'<circle cx="{x}" cy="{y}" r="{r}" fill="none" class="{e}S" stroke-width="1.6"/><circle cx="{x}" cy="{y}" r="{r*0.45:.1f}" class="{e}"/>')


def act_chip(S, x, y, kind):
    """Action chip with its icon. y is the text baseline."""
    label = ACT[kind][0]
    w = tw(label, 9.5, mono=True) + 24
    sc = "childS" if kind == "wf" else "futS"
    da = ' stroke-dasharray="3 2"' if kind == "wf" else ""
    S.raw(f'<rect x="{x:.1f}" y="{y-12:.1f}" width="{w:.1f}" height="16" rx="8" fill="none" class="{sc}" stroke-width="1.2"{da}/>')
    act_icon(S, x + 10, y - 4, kind, r=3.5)
    S.text(x + 18, y, label, 9.5, "childF" if kind == "wf" else "t", mono=True)
    return w


def legend(S, y=706):
    S.cur = S.chrome
    S.raw(f'<rect x="0" y="{y-18}" width="360" height="{738-y}" class="bg"/>')
    S.raw(f'<path d="M0 {y-17.5}H360" class="ln"/>')
    x = 12
    for kind, lab in (("agent", "agent task"), ("op", "quick op"), ("wf", "workflow ▸")):
        act_icon(S, x + 4, y - 3.5, kind, r=4)
        x += 12 + S.text(x + 11, y, lab, 9.5, "m") + 9
    S.raw(f'<circle cx="{x+4.5}" cy="{y-3.5}" r="4.5" class="soft"/><circle cx="{x+4.5}" cy="{y-3.5}" r="4.5" fill="none" class="exitS" stroke-width="1.5"/>')
    x += 13 + S.text(x + 13, y, "exit", 9.5, "m") + 9
    S.raw(f'<circle cx="{x+4.5}" cy="{y-3.5}" r="4.5" fill="none" class="loopS" stroke-width="1.5" stroke-dasharray="2 1.6"/>')
    S.text(x + 13, y, "restart ↺", 9.5, "m")
    S.cur = S.top


# ------------------------------------------------------------ definition tree (run page shape)
LX, LW, TX, RX = 24, 18, 100, 348
RH, LH = 36, 24


def X(l):
    return LX + l * LW


def N(id, lane, k, t, sub, act=None, tag="", tagc="m"):
    return dict(id=id, lane=lane, k=k, t=t, sub=sub, act=act, tag=tag, tagc=tagc)


def LBL(id, t):
    return dict(id=id, k="label", t=t, lane=0)


def layout(rows, y0):
    y = y0
    pos = {}
    for r in rows:
        h = LH if r["k"] == "label" else RH
        r["top"], r["bot"], r["y"] = y, y + h, y + h / 2
        pos[r["id"]] = r
        y += h
    return pos, y


def epath(xa, ya, xl, xb, yb):
    ya2, yb2 = ya + 14, yb - 14
    if ya2 > yb2:
        ya2 = yb2 = (ya + yb) / 2
    d = f"M{xa} {ya}"
    d += f"C{xa} {ya+8:.1f} {xl} {ya2-6:.1f} {xl} {ya2:.1f}" if xl != xa else f"L{xl} {ya2:.1f}"
    d += f"L{xl} {yb2:.1f}"
    d += f"C{xl} {yb2+6:.1f} {xb} {yb-8:.1f} {xb} {yb}" if xb != xl else f"L{xb} {yb}"
    return d


def node_shape(S, r, x=None):
    x = X(r["lane"]) if x is None else x
    y, k = r["y"], r["k"]
    if k == "step":
        e = ACT[r["act"]][1]
        S.raw(f'<circle cx="{x}" cy="{y}" r="7.5" class="bg"/>')
        S.raw(f'<circle cx="{x}" cy="{y}" r="6.5" fill="none" class="{e}S" stroke-width="2"/>')
        S.raw(f'<circle cx="{x}" cy="{y}" r="3" class="{e}"/>')
    elif k == "child":
        S.raw(f'<rect x="{x-8}" y="{y-8}" width="16" height="16" rx="4" class="bg"/>')
        S.raw(f'<rect x="{x-7}" y="{y-7}" width="14" height="14" rx="3.5" class="childSoft"/>')
        S.raw(f'<rect x="{x-7}" y="{y-7}" width="14" height="14" rx="3.5" fill="none" class="childS" stroke-width="2" stroke-dasharray="3.5 2"/>')
        S.raw(f'<rect x="{x-2.5}" y="{y-2.5}" width="5" height="5" rx="1" class="childF"/>')
    elif k == "exit":
        S.raw(f'<circle cx="{x}" cy="{y}" r="6.5" class="soft"/>')
        S.raw(f'<circle cx="{x}" cy="{y}" r="6.5" fill="none" class="exitS" stroke-width="2"/>')
    elif k == "restart":
        S.raw(f'<circle cx="{x}" cy="{y}" r="7" class="bg"/>')
        S.raw(f'<circle cx="{x}" cy="{y}" r="6.5" fill="none" class="loopS" stroke-width="1.8" stroke-dasharray="2.6 2"/>')


TITLE = {"exit": lambda t: "exit · " + t, "restart": lambda t: "↺ " + t, "child": lambda t: t,
         "step": lambda t: t}


def row_text(S, r):
    y = r["y"]
    if r["k"] == "label":
        w = tw(r["t"], 9.5, mono=True) + 14
        S.raw(f'<rect x="{TX}" y="{y-8.5:.1f}" width="{w:.1f}" height="17" rx="8.5" class="bg"/>')
        S.raw(f'<rect x="{TX}" y="{y-8.5:.1f}" width="{w:.1f}" height="17" rx="8.5" fill="none" class="futS" stroke-width="1.2"/>')
        S.text(TX + 7, y + 3.3, r["t"], 9.5, "m", mono=True, maxx=RX)
        return
    tag = r["tag"] or (ACT[r["act"]][0] if r.get("act") else "")
    tagc = r["tagc"] if r["tag"] else ("childF" if r["k"] == "child" else "m")
    tagw = tw(tag, 9.5, mono=True) if tag else 0
    tcls = "m" if r["k"] == "exit" else ("loopF" if r["k"] == "restart" else ("childF" if r["k"] == "child" else "t"))
    S.text(TX, y - 2, TITLE[r["k"]](r["t"]), 12, tcls, bold=True, maxx=RX - tagw - 6)
    if tag:
        S.text(RX, y - 2, tag, 9.5, tagc, mono=True, anchor="end")
    S.text(TX, y + 12, r["sub"], 9.5, "m", mono=True, maxx=RX)


def draw_tree(S, rows, edges, y0):
    pos, yend = layout(rows, y0)
    for (a, b, via) in edges:
        A, B = pos[a], pos[b]
        ra = 9 if A["k"] == "child" else 8
        rb = 9 if B["k"] == "child" else 8
        cls = "rst" if B["k"] == "restart" else "edge"
        d = epath(X(A["lane"]), A["y"] + ra, X(via), X(B["lane"]), B["y"] - rb)
        S.raw(f'<path d="{d}" class="{cls}"/>', "edges")
    for r in rows:
        S.check = r["bot"] > S.clip_top + 2
        if r["k"] != "label":
            node_shape(S, r)
        row_text(S, r)
        S.check = True
    return pos, yend


def level1():
    rows = [
        N("spec", 0, "step", "Write spec", "→ Split tasks", "agent"),
        N("split", 0, "step", "Split tasks", "→ 3 branches", "agent"),
        LBL("ls", "═ split · parallel · 3 branches"),
        N("api", 0, "step", "API task", "→ Integrate", "agent"),
        N("ui", 1, "step", "UI task", "→ Integrate", "agent"),
        N("docs", 2, "step", "Docs task", "→ Integrate", "agent"),
        N("integ", 0, "step", "Integrate", "join · wait for all 3 → Verify ▸", "op"),
        N("verify", 0, "child", "Verify ▸", "3 ways out: approved, design wrong, limit", "wf"),
        N("rs", 1, "restart", "restart", "if Verify ends: design wrong", tag="change", tagc="loopF"),
        N("esc", 2, "exit", "Escalate", "if Verify ends: limit", tag="exit"),
        N("rel", 0, "step", "Release", "if Verify ends: approved → exit done", "op"),
        N("done", 0, "exit", "done", "output: release-note.md", tag="exit"),
    ]
    edges = [("spec", "split", 0), ("split", "api", 0), ("split", "ui", 1), ("split", "docs", 2),
             ("api", "integ", 0), ("ui", "integ", 1), ("docs", "integ", 2), ("integ", "verify", 0),
             ("verify", "rs", 1), ("verify", "esc", 2), ("verify", "rel", 0), ("rel", "done", 0)]
    return rows, edges


def tmpl_chip(S, x, y, label, name, w):
    """A tappable template chip. y is the top. Returns nothing."""
    S.raw(f'<rect x="{x}" y="{y}" width="{w}" height="26" rx="8" class="soft"/>')
    S.text(x + 9, y + 17, label, 9, "m", mono=True, bold=True)
    lw = tw(label, 9, mono=True, bold=True)
    doc_icon(S, x + 15 + lw, y + 6.5)
    S.text(x + 31 + lw, y + 17, name, 9.5, "t", mono=True, maxx=x + w - 14)
    chev(S, x + w - 9, y + 13, "futS")


def wdef_header(S, crumb=None):
    S.cur = S.chrome
    back(S)
    if crumb:
        S.text(30, 31, crumb, 10.5, "nowF", maxx=200)
        S.text(16, 58, "Feature delivery", 16, "t", bold=True, maxx=300)
        y0 = 22
    else:
        S.text(38, 31, "Feature delivery", 16, "t", bold=True, maxx=300)
        y0 = 0
    if not crumb:
        S.text(348, 31, "Edit", 11, "nowF", bold=True, anchor="end")
    S.text(16 if crumb else 38, 52 + y0, "Ship one feature: spec, build, verify, release", 10.5, "m", maxx=348)
    y = 64 + y0
    tmpl_chip(S, 12, y, "IN", "feature-request.md", 164)
    tmpl_chip(S, 184, y, "OUT", "release-note.md", 164)
    S.raw(f'<path d="M0 {y+37.5}H360" class="ln"/>')
    S.cur = S.top
    return y + 38


def dim(S, top):
    S.raw(f'<rect x="0" y="{top}" width="360" height="{720-top}" class="dim"/>', "chrome")


def sheet(S, y):
    S.raw(f'<rect x="0" y="{y-4}" width="360" height="{724-y}" rx="18" class="shadow"/>')
    S.raw(f'<rect x="0" y="{y}" width="360" height="{740-y}" rx="18" class="soft"/>')
    S.raw(f'<rect x="162" y="{y+8}" width="36" height="4" rx="2" class="m" opacity=".5"/>')


def select_row(S, r):
    S.cur = S.chrome
    for c in ("bg", "selSoft"):
        S.raw(f'<rect x="{TX-10}" y="{r["top"]+1:.1f}" width="{RX-TX+14}" height="{r["bot"]-r["top"]-2:.1f}" rx="8" class="{c}"/>')
    S.raw(f'<rect x="{TX-10}" y="{r["top"]+1:.1f}" width="{RX-TX+14}" height="{r["bot"]-r["top"]-2:.1f}" rx="8" fill="none" class="selS" stroke-width="2"/>')
    node_shape(S, r)
    S.raw(f'<circle cx="{X(r["lane"])}" cy="{r["y"]}" r="11.5" fill="none" class="selS" stroke-width="2.2"/>')
    row_text(S, r)
    S.cur = S.top


# ------------------------------------------------------------ W-2 pages
def wdef_a():
    S = SVG()
    y0 = wdef_header(S)
    S.clip_top = y0
    rows, edges = level1()
    pos, yend = draw_tree(S, rows, edges, y0 + 6)
    y = yend + 18
    S.raw(f'<path d="M16 {y-10}H344" class="ln"/>')
    S.text(16, y + 4, "8 steps · 1 child flow ▸ · 2 exits · 1 restart ↺", 9.5, "m", mono=True)
    S.text(16, y + 22, "Tap a step for its action, input and output.", 10, "m")
    legend(S)
    return S


def wdef_b():
    S = SVG()
    y0 = wdef_header(S)
    S.clip_top = y0
    rows, edges = level1()
    pos, yend = draw_tree(S, rows, edges, y0 + 6)
    dim(S, y0)
    select_row(S, pos["verify"])
    S.cur = S.chrome
    y = pos["verify"]["bot"] + 30
    sheet(S, y)
    w = S.text(16, y + 40, "Verify ▸", 17, "childF", bold=True)
    S.text(344, y + 40, "step 7 of 8", 10, "m", mono=True, anchor="end")
    yy = y + 66
    S.text(16, yy, "action", 9.5, "m", mono=True)
    act_chip(S, 86, yy + 1, "wf")
    yy += 22
    for k, name in (("input", "build.md"), ("output", "verdict.md")):
        S.text(16, yy, k, 9.5, "m", mono=True)
        doc_icon(S, 86, yy - 10)
        S.text(102, yy, name, 10.5, "t", mono=True)
        S.text(344, yy, "template", 9.5, "m", mono=True, anchor="end")
        yy += 20
    yy += 8
    section(S, yy, "WAYS OUT")
    yy += 8
    outs = [("approved", "→", "Release", "t"), ("design wrong", "↺", "restart · change", "loopF"),
            ("limit", "→", "exit Escalate", "m")]
    card(S, 16, yy, 328, 3 * 22 + 6)
    for i, (a, ar, b, c) in enumerate(outs):
        ry = yy + 19 + i * 22
        S.raw(f'<circle cx="30" cy="{ry-3.5}" r="4" class="soft"/><circle cx="30" cy="{ry-3.5}" r="4" fill="none" class="exitS" stroke-width="1.4"/>')
        S.text(42, ry, a, 10.5, "t", bold=True)
        S.text(140, ry, ar, 10.5, "loopF" if ar == "↺" else "m")
        S.text(156, ry, b, 10.5, c, bold=c != "m", maxx=340)
    yy += 3 * 22 + 6 + 14
    button(S, 16, yy, 140, "Step definition", False)
    button(S, 164, yy, 180, "Open Verify workflow", True)
    S.cur = S.top
    S.sheet_end = yy + 38
    return S


def wdef_c():
    S = SVG()
    y0 = wdef_header(S, crumb="Pick a workflow")
    S.clip_top = y0
    rows, edges = level1()
    pos, yend = draw_tree(S, rows, edges, y0 + 6)
    S.cur = S.chrome
    y = 628
    S.raw(f'<rect x="0" y="{y}" width="360" height="92" class="bg"/><path d="M0 {y+0.5}H360" class="ln"/>')
    S.text(16, y + 22, "New session · export-button", 10, "m", mono=True)
    button(S, 16, y + 34, 100, "Back", False, h=44)
    button(S, 124, y + 34, 220, "Start", True, h=44)
    S.cur = S.top
    if yend > y:
        S.issues.append(f"tree under the start bar: {yend}")
    return S


# ------------------------------------------------------------ W-1 gallery
WFS = [("Feature delivery", "Spec, build in parallel, verify, release"),
       ("Planning", "Ask questions, write the spec, propose tasks"),
       ("Incident triage", "Triage, mitigate, then write a postmortem"),
       ("PR review", "Check a pull request and give a verdict"),
       ("Verify", "Test, review and fix until approved"),
       ("Question round", "Ask one question, get the answer, record it")]


def search(S, y, text=None, x=12, w=336, fill="soft"):
    S.raw(f'<rect x="{x}" y="{y}" width="{w}" height="34" rx="10" class="{fill}"/>')
    S.raw(f'<circle cx="{x+18}" cy="{y+16}" r="5.5" fill="none" class="futS" stroke-width="1.6"/><path d="M{x+22} {y+20}L{x+26} {y+24}" class="futS" stroke-width="1.8"/>')
    if text:
        S.text(x + 34, y + 21.5, text, 12, "t")
        S.raw(f'<rect x="{x+34+0.9*tw(text,12)+2:.1f}" y="{y+9}" width="1.5" height="16" class="nowF"/>')
        S.raw(f'<circle cx="{x+w-18}" cy="{y+17}" r="8" class="lnF"/><path d="M{x+w-21} {y+14}L{x+w-15} {y+20}M{x+w-15} {y+14}L{x+w-21} {y+20}" class="futS" stroke-width="1.4"/>')
    else:
        S.text(x + 34, y + 21.5, "Search workflows", 12, "m")


def hl_text(S, x, y, s, fs, cls, q, bold=False, maxx=330):
    i = s.lower().find(q) if q else -1
    if i >= 0:
        x1 = x + 0.9 * tw(s[:i], fs, bold=bold)
        w = 0.9 * tw(s[i:i + len(q)], fs, bold=bold)
        S.raw(f'<rect x="{x1-1:.1f}" y="{y-fs+1:.1f}" width="{w+2:.1f}" height="{fs+3:.1f}" rx="3" class="nowSoft"/>')
    S.text(x, y, s, fs, cls, bold=bold, maxx=maxx)


def gal_rows(S, items, y, rh=60, q=None, radio=None):
    for i, (n, d) in enumerate(items):
        top = y + i * rh
        tx = 16
        if radio is not None:
            tx = 46
            on = n == radio
            if on:
                S.raw(f'<rect x="8" y="{top+3}" width="344" height="{rh-6}" rx="10" class="selSoft"/>')
                S.raw(f'<rect x="8" y="{top+3}" width="344" height="{rh-6}" rx="10" fill="none" class="selS" stroke-width="1.8"/>')
            S.raw(f'<circle cx="26" cy="{top+rh/2}" r="8" fill="none" class="{"selS" if on else "futS"}" stroke-width="1.6"/>')
            if on:
                S.raw(f'<circle cx="26" cy="{top+rh/2}" r="4.2" class="selF"/>')
        hl_text(S, tx, top + rh / 2 - 3, n, 13, "t", q, bold=True)
        if d:
            hl_text(S, tx, top + rh / 2 + 14, d, 10.5, "m", q)
        chev(S, 340, top + rh / 2, "futS")
        if radio is None or n != radio:
            if i < len(items) - 1:
                S.raw(f'<path d="M16 {top+rh-0.5}H344" class="ln"/>')
    return y + len(items) * rh


def gal_a():
    S = SVG()
    S.text(16, 34, "Workflows", 20, "t", bold=True)
    S.text(16, 52, "6 workflows · a tap opens the definition", 10, "m")
    search(S, 64)
    y = gal_rows(S, WFS, 108)
    S.text(16, y + 24, "A workflow runs inside a session.", 10, "m")
    S.text(16, y + 39, "Start one from the new session form.", 10, "m")
    return S


def gal_b():
    S = SVG()
    # dimmed new session form behind
    back(S)
    S.text(38, 31, "New session", 16, "t", bold=True)
    for i, (k, v) in enumerate((("Name", "export-button"), ("Repo", "app-web"), ("Workflow", "none"))):
        yy = 62 + i * 52
        S.text(16, yy, k, 9.5, "m", mono=True)
        S.raw(f'<rect x="12" y="{yy+6}" width="336" height="32" rx="8" class="soft"/>')
        S.text(24, yy + 27, v, 12, "t" if k != "Workflow" else "m")
    dim(S, 0)
    S.cur = S.chrome
    y = 150
    sheet(S, y)
    S.text(16, y + 40, "Pick a workflow", 17, "t", bold=True)
    S.text(344, y + 40, "6", 10, "m", mono=True, anchor="end")
    search(S, y + 52, fill="bg")
    items = [("No workflow", "A plain session")] + WFS
    yend = gal_rows(S, items, y + 94, rh=54, radio="Feature delivery")
    yb = yend + 8
    button(S, 16, yb, 110, "Cancel", False)
    button(S, 134, yb, 210, "Use Feature delivery", True)
    S.sheet_end = yb + 38
    S.cur = S.top
    return S


def gal_c():
    S = SVG()
    S.text(16, 34, "Workflows", 20, "t", bold=True)
    S.text(16, 52, "2 of 6 workflows match", 10, "m")
    search(S, 64, "review")
    items = [WFS[3], WFS[4]]
    y = gal_rows(S, items, 108, q="review")
    S.raw(f'<path d="M16 {y+0.5}H344" class="ln"/>')
    S.text(16, y + 26, "Search looks in the name and the description.", 10, "m")
    # empty state below, as a second example
    y += 70
    S.raw(f'<path d="M0 {y-14}H360" class="ln" stroke-dasharray="4 4"/>')
    S.text(16, y + 6, "NO MATCH", 9, "m", mono=True, bold=True)
    search(S, y + 16, "deploy")
    cy = y + 120
    S.raw(f'<circle cx="180" cy="{cy}" r="26" class="soft"/>')
    S.raw(f'<circle cx="176" cy="{cy-3}" r="9" fill="none" class="futS" stroke-width="2"/><path d="M183 {cy+4}L190 {cy+11}" class="futS" stroke-width="2.4"/>')
    S.text(180, cy + 50, "No workflow matches “deploy”", 13, "t", bold=True, anchor="middle")
    S.text(180, cy + 70, "Try a shorter word, or clear the search.", 10.5, "m", anchor="middle")
    button(S, 120, cy + 86, 120, "Clear search", False)
    return S


# ------------------------------------------------------------ W-2a step definition pages
def sdef_header(S, name, ncls, owner, kind):
    back(S)
    S.text(38, 31, name, 17, ncls, bold=True, maxx=300)
    S.text(348, 31, "Edit", 11, "nowF", bold=True, anchor="end")
    w = S.text(38, 54, "in", 10, "m")
    x = 38 + w + 5
    x += S.text(x, 54, owner, 10.5, "childF" if "▸" in owner else "nowF", bold=True) + 8
    act_chip(S, x, 55, kind)
    S.raw('<path d="M0 70.5H360" class="ln"/>')
    return 92


def docs_card(S, y, names):
    h = 30 * len(names)
    card(S, 16, y, 328, h)
    for i, n in enumerate(names):
        ry = y + i * 30
        doc_icon(S, 28, ry + 8)
        S.text(46, ry + 19.5, n, 10.5, "t", mono=True)
        S.text(318, ry + 19.5, "template", 9.5, "nowF", mono=True, anchor="end")
        chev(S, 332, ry + 15, "nowS")
        if i:
            S.raw(f'<path d="M26 {ry+0.5}H334" class="ln"/>')
    return y + h


def prompt_card(S, y, lines):
    h = 18 * len(lines) + 34
    card(S, 16, y, 328, h)
    for i, l in enumerate(lines):
        S.text(28, y + 20 + i * 18, l, 10.5, "t", maxx=334)
    S.text(28, y + h - 10, "Show all ›", 10.5, "nowF", bold=True)
    S.text(334, y + h - 10, "prompt · 24 lines", 9.5, "m", mono=True, anchor="end")
    return y + h


def ways_card(S, y, items):
    """items: (result, arrow, target, kind, cls). kind = icon for the target."""
    h = 26 * len(items) + 8
    card(S, 16, y, 328, h)
    for i, (res, ar, tgt, kind, c) in enumerate(items):
        ry = y + 21 + i * 26
        w = chip(S, 26, ry, res, "futS", "t")
        S.text(140, ry, ar, 11, "loopF" if ar == "↺" else "m")
        ix = 160
        if kind == "exit":
            S.raw(f'<circle cx="{ix}" cy="{ry-4}" r="4.5" class="soft"/><circle cx="{ix}" cy="{ry-4}" r="4.5" fill="none" class="exitS" stroke-width="1.5"/>')
        elif kind == "restart":
            S.raw(f'<circle cx="{ix}" cy="{ry-4}" r="4.5" fill="none" class="loopS" stroke-width="1.5" stroke-dasharray="2 1.6"/>')
        else:
            act_icon(S, ix, ry - 4, kind, r=4.5)
        S.text(ix + 10, ry, tgt, 10.5, c, bold=True, maxx=340)
    return y + h


def sdef_a():
    S = SVG()
    y = sdef_header(S, "Review", "t", "Verify ▸", "agent")
    section(S, y, "INPUT TEMPLATE")
    y = docs_card(S, y + 8, ["test-report.md", "build.md"]) + 24
    section(S, y, "OUTPUT TEMPLATE")
    y = docs_card(S, y + 8, ["verdict.md"]) + 24
    section(S, y, "WHAT IT DOES")
    y = prompt_card(S, y + 8, ["Show the diff and the test report to the user.",
                               "Ask for a verdict: approve, change or design",
                               "wrong. A change needs a note."]) + 24
    section(S, y, "WAYS OUT")
    y = ways_card(S, y + 8, [("approve", "→", "exit approved", "exit", "m"),
                             ("change", "→", "Fix", "agent", "t"),
                             ("design wrong", "→", "exit design wrong", "exit", "m")])
    y += 20
    S.raw(f'<rect x="16" y="{y-13}" width="328" height="34" rx="8" fill="none" class="nowS" stroke-width="1.1" stroke-dasharray="4 3"/>')
    S.text(26, y + 1, "Open item O-1: how a result picks", 10, "nowF", maxx=338)
    S.text(26, y + 15, "its way out. Not decided yet.", 10, "nowF", maxx=338)
    return S


def sdef_b():
    S = SVG()
    y = sdef_header(S, "Verify ▸", "childF", "Feature delivery", "wf")
    section(S, y, "INPUT TEMPLATE")
    y = docs_card(S, y + 8, ["build.md"]) + 24
    section(S, y, "OUTPUT TEMPLATE")
    y = docs_card(S, y + 8, ["verdict.md"]) + 24
    section(S, y, "WHAT IT DOES · PICKED WORKFLOW")
    y += 8
    h = 80
    S.raw(f'<rect x="16" y="{y}" width="328" height="{h}" rx="8" class="childSoft"/>')
    S.raw(f'<rect x="16" y="{y}" width="328" height="{h}" rx="8" fill="none" class="childS" stroke-width="1.2" stroke-dasharray="4 3"/>')
    act_icon(S, 34, y + 19, "wf", r=6)
    S.text(48, y + 23, "Verify", 13, "t", bold=True)
    S.text(334, y + 23, "3 steps · 3 exits", 9.5, "m", mono=True, anchor="end")
    S.text(28, y + 42, "Test, review and fix until approved", 10.5, "m", maxx=334)
    S.text(28, y + 66, "Open Verify workflow ›", 11, "nowF", bold=True)
    y += h + 24
    section(S, y, "WAYS OUT · EACH EXIT OF VERIFY")
    y = ways_card(S, y + 8, [("approved", "→", "Release", "op", "t"),
                             ("design wrong", "→", "restart · change", "restart", "loopF"),
                             ("limit", "→", "exit Escalate", "exit", "m")]) + 24
    section(S, y, "RESTART ↺ OF FEATURE DELIVERY")
    y += 8
    card(S, 16, y, 328, 48)
    S.raw(f'<circle cx="32" cy="{y+16}" r="4.5" fill="none" class="loopS" stroke-width="1.5" stroke-dasharray="2 1.6"/>')
    S.text(44, y + 20, "change", 10.5, "loopF", bold=True)
    S.text(110, y + 20, "limit 2", 10.5, "t", mono=True)
    S.text(334, y + 20, "then exit Escalate", 10, "m", anchor="end")
    S.text(28, y + 38, "Write spec sees why the last pass ended.", 10, "m", maxx=334)
    return S


def sdef_c():
    S = SVG()
    y = sdef_header(S, "Fix", "t", "Verify ▸", "agent")
    section(S, y, "INPUT TEMPLATE · ONE OF")
    y = docs_card(S, y + 8, ["test-report.md", "review-note.md"]) + 24
    section(S, y, "OUTPUT TEMPLATE")
    y = docs_card(S, y + 8, ["fix-note.md"]) + 24
    section(S, y, "WHAT IT DOES")
    y = prompt_card(S, y + 8, ["Read the failed tests or the review note.",
                               "Fix the code on the branch. Write what you",
                               "changed and why."]) + 24
    section(S, y, "WAYS OUT")
    y = ways_card(S, y + 8, [("done", "→", "restart Verify", "restart", "loopF"),
                             ("at a limit", "→", "exit limit", "exit", "m")]) + 24
    section(S, y, "RESTART ↺ SETTINGS")
    y += 8
    h = 3 * 22 + 8
    card(S, 16, y, 328, h)
    hy = y + 17
    for x, t in ((28, "reason"), (110, "when"), (236, "limit")):
        S.text(x, hy, t, 9, "m", mono=True)
    S.raw(f'<path d="M26 {hy+6}H334" class="ln"/>')
    for i, (rs, wh, lim) in enumerate((("error", "after a fail", "3"), ("change", "after a change", "3"))):
        ry = hy + 24 + i * 22
        S.raw(f'<circle cx="32" cy="{ry-3.5}" r="4" fill="none" class="loopS" stroke-width="1.4" stroke-dasharray="2 1.5"/>')
        S.text(42, ry, rs, 10.5, "loopF", bold=True)
        S.text(110, ry, wh, 10.5, "t")
        S.text(236, ry, lim, 10.5, "t", mono=True)
        S.text(334, ry, "→ exit limit", 9.5, "m", mono=True, anchor="end")
    y += h + 18
    S.text(16, y, "Step 1 Test sees the reason of the last pass.", 10, "m")
    return S


def main():
    jobs = {"gal-a.svg": gal_a, "gal-b.svg": gal_b, "gal-c.svg": gal_c,
            "wdef-a.svg": wdef_a, "wdef-b.svg": wdef_b, "wdef-c.svg": wdef_c,
            "sdef-a.svg": sdef_a, "sdef-b.svg": sdef_b, "sdef-c.svg": sdef_c}
    bad = False
    for name, fn in jobs.items():
        S = fn()
        se = getattr(S, "sheet_end", None)
        if se and se > 712:
            print(name, "sheet too tall", se); bad = True
        if S.end > 712:
            print(name, "content too low", S.end); bad = True
        for i in S.issues:
            print(name, i); bad = True
        print(name, "end", round(S.end), "sheet", se)
        with open(os.path.join(OUT, name), "w", encoding="utf-8") as fh:
            fh.write(S.out())
    meta = {"rows": [
        {"id": "r5gal", "name": "W-1 · Workflows",
         "why": "A plain gallery of the workflows: the name and one line about what it does. A search field filters the list. The same list is the picker of the new session form. A tap opens the definition. No runs show here: runs belong to sessions.",
         "sketches": [
             {"file": "gal-a.svg", "caption": "Gallery · 6 workflows, search"},
             {"file": "gal-b.svg", "caption": "Picker · from the new session form"},
             {"file": "gal-c.svg", "caption": "Search: 2 matches, and no match"},
         ]},
        {"id": "r5wdef", "name": "W-2 · Workflow definition",
         "why": "The run page shape with no run data. The header has the name, the description, and the input and output templates. Each row shows the step, its action and its ways out. A tap opens a sheet. From the picker, a Start button shows.",
         "sketches": [
             {"file": "wdef-a.svg", "caption": "Feature delivery · the flow graph"},
             {"file": "wdef-b.svg", "caption": "Tap on Verify ▸ · step sheet"},
             {"file": "wdef-c.svg", "caption": "From the picker · Start button"},
         ]},
        {"id": "r5sdef", "name": "W-2a · Step definition",
         "why": "One page for each step: the owner workflow and the action on top, then the input and output templates, what it does, and the ways out. A start workflow ▸ step shows the picked workflow and the exit map. Restart settings show only when the step restarts.",
         "sketches": [
             {"file": "sdef-a.svg", "caption": "Review · agent task, 3 ways out"},
             {"file": "sdef-b.svg", "caption": "Verify ▸ · start workflow, exits"},
             {"file": "sdef-c.svg", "caption": "Fix · restart error and change"},
         ]},
    ]}
    for row in meta["rows"]:
        assert len(row["why"].split()) <= 50, (row["id"], len(row["why"].split()))
        for s in row["sketches"]:
            assert len(s["caption"]) <= 36, s
    with open(os.path.join(OUT, "r5-def.json"), "w", encoding="utf-8") as fh:
        json.dump(meta, fh, ensure_ascii=False, indent=2)
    print("bad" if bad else "ok")


main()
