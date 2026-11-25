import os,json
D=os.path.dirname(os.path.abspath(__file__))
src=open(os.path.join(D,'..','r5b','gen.py'),encoding='utf-8').read().split('# ------------------------------------------------------------ run page header')[0]
src=src.replace('"..", "..", "r5")','"..", "..", "f3")')
exec(src)
_l=link
def link(S,x,y,label,fs=11,maxx=344): S.text(x,y,label+' ›',fs,'selF',maxx=maxx)
P={41:("Export service","task","export-api","+210 −12"),42:("CSV delimiter","decision","export-api","+14 −3"),
 43:("Export button","task","export-ui","+340 −40"),44:("Button place","decision","export-ui","+22 −8"),
 45:("Export docs","task","export-docs","+80")}
KC={"task":"agent","decision":"childF"}
def head(S,title,sub,right=None):
    S.raw('<path d="M22 18L15 26L22 34" class="edge" stroke-width="2"/>')
    S.text(38,28,title,15,"t",bold=True,maxx=300)
    S.text(38,46,sub,10.5,"m")
    if right: S.text(346,28,right,10.5,"humanT",mono=True,anchor="end",bold=True)
    S.raw('<path d="M0 58.5H360" class="ln"/>')
def toggle(S,sel,y=68):
    rect(S,12,y,336,28,14,"soft")
    for i,l in enumerate(["Tree","Cards"]):
        if l==sel:
            rect(S,14+i*166,y+2,166,24,12,"bg"); frame(S,14+i*166,y+2,166,24,12,"nowS",1.2)
        S.text(97+i*166,y+18,l,11,"nowF" if l==sel else "m",bold=l==sel,anchor="middle")
def legend(S):
    x=14
    for c,l in [("agent","task PR"),("childF","decision PR")]:
        S.raw(f'<circle cx="{x}" cy="700" r="4" class="{c}"/>'); x+=8; x+=S.text(x,704,l,9.5,"m")+16
    S.text(346,704,"tap a row: preview",9.5,"m",anchor="end")
def edge(S,x1,y1,x2,y2,c="edge"):
    if x1==x2: S.raw(f'<path d="M{x1} {y1}V{y2}" class="{c}"/>')
    else: S.raw(f'<path d="M{x1} {y1}C{x1} {y1-30} {x2} {y1-14} {x2} {y1-44}V{y2}" class="{c}"/>')
def node(S,x,y,kind,now=False,state=""):
    if now: S.raw(f'<circle cx="{x}" cy="{y}" r="14" class="nowSoft"/>')
    S.raw(f'<circle cx="{x}" cy="{y}" r="9" class="bg"/>')
    sc={"merged":"passS","pre":"passS","change":"failS"}.get(state,"nowS" if now else "futS")
    d=' stroke-dasharray="3 2"' if state=="pre" else ""
    S.raw(f'<circle cx="{x}" cy="{y}" r="8" fill="none" class="{sc}" stroke-width="{3 if now else 1.6}"{d}/><circle cx="{x}" cy="{y}" r="4" class="{KC[kind]}"/>')
ST={"":("open","m"),"you":("waits for you","humanT"),"pre":("pre-approved","passF"),"change":("change asked","failF"),"merged":("merged ✓","passF"),"next":("next","nowF")}
def row(S,n,x,y,state="",now=False,tx=70):
    t,k,s,d=P[n]
    if now:
        rect(S,8,y-24,344,48,10,"nowSoft"); rect(S,2,y-20,4,40,2,"nowF")
    node(S,x,y,k,now,state)
    S.text(tx,y-2,f"#{n}",10,"m",mono=True)
    S.text(tx+30,y-2,t,12.5,"m" if state=="merged" else "t",bold=True)
    S.text(tx,y+14,f"{k} · {s}",9.5,"m",mono=True)
    l,c=ST[state]; S.text(346,y-2,l,9.5,c,mono=True,anchor="end",bold=now)
def base(S,y,label="main",x=28):
    rect(S,x-7,y-7,14,14,3,"soft"); frame(S,x-7,y-7,14,14,3,"exitS",1.6)
    S.text(78,y+4,label,11.5,"m",mono=True)
def save(S,f):
    if S.issues: print(f,S.issues)
    open(os.path.join(OUT,f),'w',encoding='utf-8',newline='\n').write(S.out())
Y={44:140,43:226,45:312,42:398,41:484}
def tree_(S,st,now,basey=570):
    edge(S,28,basey,28,Y[44]); edge(S,28,Y[42],50,Y[45])
    for n,y in Y.items(): row(S,n,50 if n==45 else 28,y,st.get(n,""),n==now,78)
# S-1
S=SVG(); head(S,"Export stream","5 PRs · 1 waits for you","you"); toggle(S,"Tree"); tree_(S,{41:"you"},41); base(S,570)
S.text(78,592,"base · the PRs merge from the bottom",9.5,"m"); button(S,12,630,336,"Review #41 · the lowest","pri"); legend(S); save(S,"tree-a.svg")
S=SVG(); head(S,"Export stream","5 PRs · 1 merged · 1 pre-approved","you"); toggle(S,"Tree"); tree_(S,{41:"merged",42:"you",43:"pre",44:"change"},42)
base(S,570); S.text(78,592,"#43 merges when it is the lowest",9.5,"m"); button(S,12,630,336,"Review #42 · the lowest","pri"); legend(S); save(S,"tree-b.svg")
S=SVG(); head(S,"Export stream","tree and cards together","you")
ys={44:96,43:156,45:216,42:276}
edge(S,28,650,28,96); edge(S,28,276,50,216)
for n,y in ys.items(): row(S,n,50 if n==45 else 28,y,"",False,78)
rect(S,70,306,282,306,12,"nowSoft"); frame(S,70,306,282,306,12,"nowS",1.4)
node(S,28,336,"task",True)
S.text(84,332,"#41",10,"m",mono=True); S.text(114,332,"Export service",14,"t",bold=True); S.text(84,350,"task · export-api · +210 −12",9.5,"m",mono=True)
S.text(84,378,"SUMMARY",9,"m",mono=True,bold=True)
for i,l in enumerate(["Adds the export service and its tests.","Reads the rows in pages."]): S.text(84,396+i*16,l,11,"t")
S.text(84,440,"KEY INFORMATION",9,"m",mono=True,bold=True)
for i,l in enumerate(["43 tests passed","New public API: 1 method","No default answer in this PR"]): S.text(84,458+i*17,"· "+l,11,"t")
link(S,84,524,"Preview"); S.text(84,548,"Approve merges #41",9.5,"m")
buttons(S,82,564,258,["Approve","Comment","Reject"])
base(S,650); S.text(346,654,"base",9.5,"m",anchor="end"); legend(S); save(S,"tree-c.svg")
# S-2
def card(S,n,y,h,now,lines,key,acts,state):
    t,k,s,d=P[n]
    rect(S,12,y,336,h,12,"bg"); rect(S,12,y,336,h,12,"nowSoft" if now else "soft"); frame(S,12,y,336,h,12,"nowS" if now else "lnS",1.4 if now else 1)
    S.raw(f'<circle cx="28" cy="{y+20}" r="4.5" class="{KC[k]}"/>')
    S.text(40,y+24,f"#{n}",10,"m",mono=True); S.text(70,y+24,t,13.5,"t",bold=True)
    l,c=ST[state]; S.text(338,y+24,l,9.5,c,mono=True,anchor="end",bold=True)
    S.text(24,y+42,f"{k} · {s} · {d}",9.5,"m",mono=True)
    yy=y+62
    for x in lines: S.text(24,yy,x,11,"t"); yy+=16
    for x in key: S.text(24,yy+4,"· "+x,10.5,"m"); yy+=16
    if acts: buttons(S,22,y+h-42,316,acts)
def peek(S):
    for i in (2,1):
        rect(S,12+i*8,154-i*10,336-i*16,60,12,"soft"); frame(S,12+i*8,154-i*10,336-i*16,60,12,"lnS",1)
def pager(S,n):
    S.text(180,570,f"‹   {n} of 5   ›",12,"nowF",mono=True,anchor="middle"); S.text(180,592,"swipe to see a higher PR",9.5,"m",anchor="middle")
S=SVG(); head(S,"Export stream","card 1 of 5 · the lowest first","you"); toggle(S,"Cards"); peek(S)
card(S,41,154,380,True,["Adds the export service and its tests.","The service reads the rows in pages."],[],["Approve","Comment","Reject"],"you")
S.text(24,276,"KEY INFORMATION",9,"m",mono=True,bold=True)
for i,l in enumerate(["43 tests passed, 0 failed","New public API: 1 method","No default answer in this PR"]): S.text(24,296+i*18,"· "+l,11,"t")
S.text(24,372,"AFTER THIS PR",9,"m",mono=True,bold=True); S.text(24,392,"#42 CSV delimiter comes next",11,"t")
link(S,24,426,"Open the preview"); S.text(180,480,"Approve merges #41",9.5,"m",anchor="middle"); pager(S,1); save(S,"card-a.svg")
S=SVG(); head(S,"Export stream","5 cards · the lowest on top","you"); toggle(S,"Cards")
card(S,41,108,156,True,["Adds the export service and its tests."],["43 tests passed · new API: 1 method"],["Approve","Comment","Reject"],"you")
y=276
for n,s in [(42,"next"),(45,""),(43,"pre"),(44,"change")]:
    card(S,n,y,96,False,[{42:"Default: comma. Needs your review.",45:"Guide for the export page.",43:"Adds the button to the toolbar.",44:"You asked: put it in the menu."}[n]],[],None,s)
    link(S,276,y+84,"Preview"); y+=106
save(S,"card-b.svg")
S=SVG(); head(S,"Export stream","card 2 of 5 · the lowest first","you"); toggle(S,"Cards")
rect(S,12,108,336,32,10,"childSoft"); S.text(24,128,"✓ #41 Export service approved and merged",11,"passF",bold=True)
card(S,42,154,380,True,["The session did not get an answer in time.","It used the default."],[],["Approve","Comment","Reject"],"you")
S.text(24,272,"THE DECISION",9,"m",mono=True,bold=True); S.text(24,294,"Which delimiter does the CSV use?",12,"t",bold=True)
for i,(l,c) in enumerate([("comma (the default, in this PR)","passF"),("semicolon","m"),("tab","m")]): S.text(24,318+i*20,("● " if i==0 else "○ ")+l,11,c)
S.text(24,392,"Risk: low · 3 PRs are on top of it",10.5,"m"); link(S,24,426,"Open the preview"); S.text(180,480,"Approve merges #42",9.5,"m",anchor="middle"); pager(S,2); save(S,"card-c.svg")
# S-3
def body_(S,y,body):
    for h,ls in body:
        S.text(16,y,h,9,"m",mono=True,bold=True); y+=18
        for l in ls:
            if l.startswith("["):
                rect(S,16,y-13,328,25,6,"soft"); frame(S,16,y-13,328,25,6,"lnS",1); S.text(24,y+3,l[1:],10.5,"m"); y+=30
            else: S.text(16,y,l,11,"t"); y+=16
        y+=10
    return y
def sheet(S,n,top,state,body,acts,note):
    rect(S,0,0,360,720,0,"dim"); rect(S,0,top,360,740-top,18,"bg"); rect(S,162,top+8,36,4,2,"m",' opacity=".5"')
    t,k,s,d=P[n]; y=top+40
    S.text(16,y,f"#{n}",11,"m",mono=True); S.text(50,y,t,17,"t",bold=True); l,c=ST[state]; S.text(344,y,l,10,c,mono=True,anchor="end",bold=True)
    S.text(16,y+18,f"{k} · {s} · {d}",9.5,"m",mono=True)
    y=body_(S,y+46,body)
    if y>648: S.issues.append(f"sheet body ends {y}")
    link(S,278,652,"Full form"); S.text(16,652,note,9.5,"m"); buttons(S,16,664,328,acts,h=34)
def bg():
    S=SVG(); head(S,"Export stream","5 PRs · 1 merged","you"); toggle(S,"Tree"); tree_(S,{41:"merged",42:"you"},42); return S
DEC=[("CONTEXT",["The export needs a CSV format.","No answer came in time, so the default applies."]),("SUMMARY",["Sets the delimiter to a comma. 2 files change."]),
 ("KEY INFORMATION FOR THE DECISION",["· Options: comma, semicolon, tab","· Default: comma · risk low","· #43, #44 and #45 are on top of it"])]
S=bg(); sheet(S,42,170,"you",DEC+[("FIELDS",["[Your reason (optional)","[Use this answer again:  no"])],["Approve","Comment","Reject"],"Lowest PR: approve merges it."); save(S,"prev-a.svg")
S=bg(); sheet(S,43,200,"",[("CONTEXT",["The user starts an export from the toolbar.","Part of the task Export button."]),("SUMMARY",["Adds the button and its tests. 7 files change."]),
 ("KEY INFORMATION",["· 12 tests passed","· Below it: #42 (waits for you)","· Above it: #44"]),("FIELDS",["[Comment for the session"])],["Pre-approve","Comment","Reject"],"Not the lowest: it merges after #42."); save(S,"prev-b.svg")
S=SVG(); head(S,"#42 CSV delimiter","decision · export-api · +14 −3","you")
y=body_(S,82,DEC+[("CHECKS",["✓ build · 43 tests passed"]),("FILES",["CsvWriter.cs  +10 −3","CsvWriterTests.cs  +4"]),("COMMENTS",["export-api: Comma is the RFC 4180 default."]),("FIELDS",["[Your reason (optional)"])])
link(S,16,y+2,"Session export-api"); link(S,190,y+2,"Open on GitHub"); print('full end',y)
S.raw('<path d="M0 638.5H360" class="ln"/>'); S.text(16,655,"Lowest PR: approve merges it.",9.5,"m"); buttons(S,16,666,328,["Approve","Comment","Reject"],h=34); save(S,"prev-c.svg")
rows=[("f3tree","S-1 · Stack tree","The PRs of a stream as a Git tree, the base at the bottom. The lowest PR has the marker. A tap on a row opens the preview. Variation c puts the card of the lowest PR in the tree.",["All open · #41 is the lowest","Half way · pre-approved, change","Variation: tree and cards together"]),
("f3card","S-2 · Stack cards","One card for each PR, the lowest first, with the acts. Approve merges the lowest PR. A higher PR can be pre-approved. Variation b shows all cards in a list.",["One card at a time","Variation: list of cards","After an approve: the next card"]),
("f3prev","S-3 · PR preview","One PR template has three forms: card, preview and full. The preview shows the context, the summary, the key information, the fields and the acts. The template is a placeholder (O-2). The full form is F4.",["Preview of the lowest PR","Preview of a higher PR","Full form (F4)"])]
R=[{"id":i,"name":n,"why":w,"sketches":[{"file":f"{i[2:]}-{v}.svg","caption":c} for v,c in zip("abc",cs)]} for i,n,w,cs in rows]
json.dump({"rows":R},open(os.path.join(OUT,'f3.json'),'w',encoding='utf-8'),ensure_ascii=False,indent=1)
h="".join("".join(f"<img src='../../f3/{f}-{v}.svg' width=360 height=720 style='border:1px solid #999'>" for v in "abc")+"<br>" for f in ("tree","card","prev"))
open(os.path.join(D,'all.html'),'w').write("<body style='margin:0;width:1100px'>"+h)
