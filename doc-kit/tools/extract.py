"""Make the data/*.json files of the reference gallery from saved artifact pages.

Usage: python tools/extract.py <folder with the saved pages> <doc-kit folder>
Inline text uses `code` and **bold** marks only.
"""
import json, re, sys
from pathlib import Path
from bs4 import BeautifulSoup, NavigableString, Tag

SRC, ROOT = Path(sys.argv[1]), Path(sys.argv[2])
DATA = ROOT / "data"
DATA.mkdir(parents=True, exist_ok=True)


def soup(name):
    return BeautifulSoup((SRC / f"{name}.html").read_text(encoding="utf-8"), "html.parser")


def inline(node):
    """Element content as text with `code` and **bold** marks."""
    if isinstance(node, NavigableString):
        return str(node)
    out = []
    for c in node.children:
        if isinstance(c, NavigableString):
            out.append(str(c))
        elif c.name == "code":
            out.append("`" + c.get_text() + "`")
        elif c.name in ("b", "strong"):
            out.append("**" + inline(c) + "**")
        elif c.name == "br":
            out.append(" ")
        else:
            out.append(inline(c))
    return re.sub(r"\s+", " ", "".join(out)).strip()


def save(name, obj):
    (DATA / f"{name}.json").write_text(json.dumps(obj, ensure_ascii=False, indent=1), encoding="utf-8")
    print("wrote", name)


# 1. Tree -------------------------------------------------------------------
rows = json.loads((SRC / "umpire-folder-layout.folders.json").read_text(encoding="utf-8"))
tree = {
    "source": {"title": "Umpire Folder Layout", "url": "https://claude.ai/artifact/6q4JDs9ndFKhkgw3pURTNn"},
    "tags": {"later": "warn", "kind": "neutral"},
    "rows": sorted(
        [{k: r[k] for k in ("path", "desc", "kind", "when", "entry", "install", "order") if r.get(k)} for r in rows],
        key=lambda r: (r.get("order", 0), r["path"]),
    ),
}
save("tree", tree)

# 2. Columns ----------------------------------------------------------------
s = soup("vsa-split-map")
groups = []
for col in s.select("section.cols > .col"):
    gid = [c for c in col["class"] if c != "col"][0]
    items = []
    for box in col.select(".box"):
        name = box.select_one(".name")
        ref = name.select_one(".pr")
        flags = [f.get_text(strip=True) for f in name.select(".flag")]
        for x in name.select(".pr, .flag"):
            x.extract()
        item = {"name": name.get_text(" ", strip=True), "meta": box.select_one(".loc").get_text(strip=True),
                "desc": inline(box.select_one(".what"))}
        if ref:
            item["ref"] = ref.get_text(strip=True)
        if flags:
            item["flags"] = flags
        items.append(item)
    head = col.select_one("header")
    groups.append({"id": gid, "title": head.b.get_text(strip=True), "desc": head.span.get_text(strip=True), "items": items})
bar = []
for d in s.select(".bar > div"):
    m = re.search(r"flex:(\d+);background:var\(--(\w+)\)", d["style"])
    bar.append({"group": m.group(2), "value": int(m.group(1)), "label": d.get_text(strip=True)})
edges = []
h2 = s.find("h2", string=re.compile("wrong way"))
for tr in h2.find_next("table").select("tbody tr"):
    td = tr.find_all("td")
    edges.append({"flag": td[0].get_text(strip=True),
                  "from": {"text": inline(td[1]), "group": [c for c in td[1].get("class", []) if c != "from"][0]},
                  "to": {"text": inline(td[2]), "group": [c for c in td[2].get("class", []) if c != "from"][0]},
                  "problem": inline(td[3])})
save("columns", {
    "source": {"title": "VSA Split Map", "url": "https://claude.ai/artifact/WzLLoM4f8CEt4WDwzwYh1o"},
    "groups": groups,
    "bar": {"title": "Size of each group (lines of C#, about)", "parts": bar},
    "flags": {"title": "Edges that cross the wrong way", "rows": edges},
})

# 3. Graph (written from the SVG of Q6, Topology tab) -------------------------
s = soup("q6-anchor-and-host-types")
sec = s.select_one("#agreed")
save("graph", {
    "source": {"title": "Q6 Anchor and Host Types", "url": "https://claude.ai/artifact/CGKtHX8HKMssjKXaaTQZsJ"},
    "title": sec.h2.get_text(strip=True),
    "summary": inline(sec.p),
    "rows": [3, 3, 3, 4],
    "nodes": [
        {"id": "shell", "label": "Shell", "sub": "Program.cs · Unity shell", "kind": "outside", "row": 0, "col": 0},
        {"id": "app", "label": "App<TArchitecture>", "sub": "lifetime, app logic", "kind": "key", "row": 0, "col": 1},
        {"id": "engine", "label": "Engine (build)", "sub": "reads Definition", "kind": "outside", "row": 0, "col": 2},
        {"id": "arch", "label": "Architecture", "sub": "Vsa<TDefinition, THost>", "kind": "key", "row": 1, "col": 1},
        {"id": "def", "label": "Definition", "sub": "rules and layout", "kind": "data", "row": 1, "col": 2},
        {"id": "host", "label": "Host", "sub": "composition root", "kind": "plain", "row": 2, "col": 1},
        {"id": "core", "label": "Core", "sub": "kernel", "kind": "plain", "row": 3, "col": 0},
        {"id": "install", "label": "Install()", "sub": "architecture, platform", "kind": "plain", "row": 3, "col": 1},
        {"id": "anchors", "label": "Anchors", "sub": "from AppTypes", "kind": "plain", "row": 3, "col": 2},
        {"id": "appsvc", "label": "App", "sub": "ConfigureServices", "kind": "plain", "row": 3, "col": 3},
    ],
    "edges": [
        {"from": "shell", "to": "app", "label": "Run"},
        {"from": "app", "to": "arch", "label": "selects"},
        {"from": "arch", "to": "def", "dashed": True},
        {"from": "def", "to": "engine", "dashed": True},
        {"from": "arch", "to": "host", "label": "CreateHost"},
        {"from": "host", "to": "core", "label": "1"},
        {"from": "host", "to": "install", "label": "2"},
        {"from": "host", "to": "anchors", "label": "3"},
        {"from": "host", "to": "appsvc", "label": "4"},
    ],
    "legend": [{"kind": "key", "text": "our startup types"}, {"kind": "data", "text": "data for the engine"},
               {"kind": "outside", "text": "outside the app"}],
    "note": "Compose runs 1 → 4 in a fixed order. Native .NET under the host: the builder, `IHost`, `IServiceProvider`.",
})

# 4. Call stacks ------------------------------------------------------------
s = soup("orchestrator-internals")
TREE = re.compile(r"^[\s│├└─]*")


def parse_stack(pre):
    """A pre.stack as nested nodes. Depth comes from the column where the call text starts."""
    lines, cur = [], []
    for c in pre.children:
        parts = [c] if not isinstance(c, NavigableString) else []
        if isinstance(c, NavigableString):
            for i, piece in enumerate(str(c).split("\n")):
                if i:
                    lines.append(cur)
                    cur = []
                if piece:
                    cur.append(("t", piece))
        else:
            cur.append(("k" if "k" in c.get("class", []) else "c", c.get_text()))
    lines.append(cur)
    root, stack, foot = [], [], []
    for ln in lines:
        raw = "".join(t for _, t in ln)
        if not raw.strip():
            continue
        pre_txt = TREE.match(raw).group(0)
        depth = len(pre_txt) // 3 if pre_txt.strip() else 0
        call = "".join(t for k, t in ln if k != "c")
        call = call[len(pre_txt):].strip() if call.startswith(pre_txt) else call.strip()
        if re.fullmatch(r"[\s│├└─]*", call):
            call = ""
        note = " ".join(t.strip() for k, t in ln if k == "c")
        emph = any(k == "k" for k, _ in ln)
        if not call:
            if stack and note and pre_txt:
                stack[-1][1]["note"] = (stack[-1][1].get("note", "") + "\n" + note).strip()
            elif note:
                foot.append(note)
            continue
        node = {"call": call}
        if note:
            node["note"] = note
        if emph:
            node["key"] = True
        while stack and stack[-1][0] >= depth:
            stack.pop()
        (stack[-1][1].setdefault("children", []) if stack else root).append(node)
        stack.append((depth, node))
    out = {"root": root}
    if foot:
        out["foot"] = " ".join(foot)
    return out


groups = []
for sec in s.select("#calls > section"):
    g = {"title": sec.h2.get_text(strip=True), "stacks": [parse_stack(p) for p in sec.select("pre.stack")]}
    summ = sec.select_one("p.summary")
    if summ:
        g["summary"] = inline(summ)
    groups.append(g)
save("stack", {"source": {"title": "Orchestrator Internals", "url": "https://claude.ai/artifact/CP2ozBgysFuJSwhrgoHugt"},
               "groups": groups})


# 5. Folds ------------------------------------------------------------------
def body_blocks(el):
    out = []
    for c in el.children:
        if isinstance(c, NavigableString):
            if c.strip():
                out.append({"type": "p", "text": c.strip()})
            continue
        if c.name == "pre":
            out.append({"type": "code", "code": c.get_text().strip("\n")})
        elif c.name == "p":
            out.append({"type": "p", "text": inline(c)})
        elif c.name in ("ul", "ol"):
            out.append({"type": "list", "ordered": c.name == "ol", "items": [inline(li) for li in c.find_all("li", recursive=False)]})
        elif c.name == "table" or c.select_one("table"):
            t = c if c.name == "table" else c.select_one("table")
            out.append({"type": "table", "head": [inline(th) for th in t.select("thead th")],
                        "rows": [[inline(td) for td in tr.find_all("td")] for tr in t.select("tbody tr")]})
        else:
            txt = inline(c)
            if txt:
                out.append({"type": "p", "text": txt})
    return out


s = soup("unity-test-suite-map")
sec = s.select_one("#architecture")
items = []
for d in sec.find_all("details", recursive=True):
    sm = d.summary
    items.append({"title": sm.select_one(".t").get_text(strip=True), "summary": inline(sm.select_one(".f")),
                  "open": d.has_attr("open"), "body": body_blocks(d.select_one(".body"))})
save("folds", {"source": {"title": "Unity Test Suite Map", "url": "https://claude.ai/artifact/Hd1v1iskYsus8RgNWhPoFu"},
               "items": items})

# 6. Composed folds ---------------------------------------------------------
files = json.loads((ROOT.parent / "doc-kit-src-files.json").read_text(encoding="utf-8")) if (ROOT.parent / "doc-kit-src-files.json").exists() else None
if files is None:
    files = json.loads((SRC.parent / "files.json").read_text(encoding="utf-8"))
TONE = {"source": "accent", "generated": "info", "managed": "good", "scaffolded": "warn", "native": "neutral", "removed": "bad", "na": "neutral"}
WHO = ["Definition", "Engine", "CLI"]


def snip(x):
    return [{"type": "code", "cap": x["cap"], "lang": x.get("lang", ""), "code": x["code"]}] if x else []


def owners(lst):
    return {"type": "facts", "items": [{"label": WHO[i], "value": "none" if v == "-" else v,
                                        "tone": "neutral" if v == "-" else ("accent" if v == "writes" else "info")}
                                       for i, v in enumerate(lst)]}


composed = []
for f in [f for f in files if f["tab"] == "app"]:
    today = [{"type": "p", "text": f["today"]["text"]},
             {"type": "kv", "items": [{"label": "Purpose", "value": f["purpose"]}, {"label": "Without it", "value": f["without"]}]},
             owners(f["today"]["owners"])]
    if f["today"].get("ownersNote"):
        today.append({"type": "p", "text": f["today"]["ownersNote"], "muted": True})
    today += snip(f["today"].get("snip")) + snip(f["today"].get("snip2"))
    prop = [{"type": "p", "text": f["proposal"]["text"]}, owners(f["proposal"]["owners"])]
    prop += snip(f["proposal"].get("snip")) + snip(f["proposal"].get("snip2"))
    trade = [{"type": "cols", "items": [{"title": "Win", "tone": "good", "items": f["trade"]["win"]},
                                        {"title": "Lose", "tone": "bad", "items": f["trade"]["lose"]},
                                        {"title": "Change", "tone": "warn", "items": f["trade"]["change"]}]}]
    composed.append({
        "title": f["name"], "summary": f["role"], "mono": True,
        "tags": [{"text": f["purpose"], "tone": "neutral"},
                 {"text": f["now"], "tone": TONE.get(f["now"], "neutral")}, {"text": "→", "tone": "none"},
                 {"text": f["next"], "tone": TONE.get(f["next"], "neutral")}],
        "children": [
            {"title": "Today", "summary": "What it does, who owns it, what breaks without it",
             "tags": [{"text": f["now"], "tone": TONE.get(f["now"], "neutral")}], "body": today},
            {"title": "Proposal", "summary": f["proposal"]["kind"],
             "tags": [{"text": f["next"], "tone": TONE.get(f["next"], "neutral")}], "body": prop},
            {"title": "Trade-offs", "summary": "What we win, lose and change", "body": trade},
        ],
    })
save("composed", {"source": {"title": "Umpire File Map", "url": "https://claude.ai/artifact/X8c3Qx9iLgY66K77exdU3Z"},
                  "title": "13 files of an app, by state after the proposal", "items": composed})
