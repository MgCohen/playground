#!/bin/bash
ID=$(cat /c/Users/matheus.cohen/Documents/Projects/playground/session-orchestrator/spikes/p1.id | tr -d '\r\n ')
D=/c/Users/matheus.cohen/Documents/Projects/playground/session-orchestrator/store/sessions/$ID
T=$(ls -d /c/Users/matheus.cohen/.claude/projects/*/$ID.jsonl | head -1)
idle() { for i in $(seq 1 120); do grep -q '"idle"' $D/status.json 2>/dev/null && return 0; sleep 2; done; echo "TIMEOUT idle"; }
running_then_idle() { for i in $(seq 1 30); do grep -q '"running"' $D/status.json && break; sleep 1; done; idle; }
lastsay() { node -e "const l=require('fs').readFileSync(process.argv[1],'utf8').trim().split('\n').map(JSON.parse).filter(r=>r.type==='assistant');const c=l.at(-1).message.content;console.log('assistant:',c.map(x=>x.text??('tool_use:'+x.name)).join(' | '))" "$T"; }
newdecision() { for i in $(seq 1 60); do for f in $D/decisions/*.json; do [ -e "$f" ] || continue; b=$(basename $f .json); grep -qx "$b" $D/seen 2>/dev/null || { echo $b >> $D/seen; echo $b; return; }; done; sleep 2; done; }
mkdir -p $D/inbox $D/answers; touch $D/seen; for f in $D/decisions/*.json; do [ -e "$f" ] && basename $f .json >> $D/seen; done
idle
echo "== T1 inbox + pending ask_user"
echo "Inbox test 1. Call mcp__orch__ask_user with question 'Pick a fruit', options PLUM and LIME, risk low, defaultOption PLUM, waitSeconds 0. Then end your turn. When the answer message arrives later, reply only with the fruit word." > $D/inbox/1.txt
X=$(newdecision); echo "decision $X"; cat $D/decisions/$X.json | head -12; idle; lastsay
echo '{"answer":"LIME"}' > $D/answers/$X.json; running_then_idle; lastsay; cat $D/delivered/$X.json
echo "== T2 blocking ask_user"
echo "Inbox test 2. Call mcp__orch__ask_user with question 'Pick a nut', options PECAN and CASHEW, risk low, defaultOption PECAN, waitSeconds 90. Then reply only with the answer word." > $D/inbox/2.txt
X=$(newdecision); echo "decision $X"; sleep 20; echo '{"answer":"CASHEW"}' > $D/answers/$X.json; idle; lastsay; cat $D/delivered/$X.json
echo "== T3 AskUserQuestion redirect"
echo "Inbox test 3. Use the AskUserQuestion tool once: question 'Pick a berry', header 'Berry', options GOJI and ACAI. Then reply only with the answer word." > $D/inbox/3.txt
X=$(newdecision); echo "decision $X"; Q=$(node -e "console.log(JSON.parse(require('fs').readFileSync(process.argv[1],'utf8')).questions[0].question)" $D/decisions/$X.json); node -e "require('fs').writeFileSync(process.argv[1],JSON.stringify({answers:{[process.argv[2]]:'ACAI'}}))" $D/answers/$X.json "$Q"; idle; lastsay; cat $D/delivered/$X.json
echo "== T4 blocking timeout, then answer by inbox"
echo "Inbox test 4. Call mcp__orch__ask_user with question 'Pick a seed', options CHIA and FLAX, risk low, defaultOption CHIA, waitSeconds 20. If it returns PENDING, end your turn. When the answer message arrives later, reply only with the seed word." > $D/inbox/4.txt
X=$(newdecision); echo "decision $X"; idle; lastsay; grep -o '"mode": "[a-z]*"' $D/decisions/$X.json
echo '{"answer":"FLAX"}' > $D/answers/$X.json; running_then_idle; lastsay; cat $D/delivered/$X.json
echo "== summary"; ls $D/mirror | wc -l; cat $D/cursor.txt; echo; tail -15 $D/log.txt
node -e "const l=require('fs').readFileSync(process.argv[1],'utf8').trim().split('\n').map(JSON.parse).filter(r=>r.uuid);const kids=new Set(l.map(r=>r.parentUuid));console.log('leaves',l.filter(r=>!kids.has(r.uuid)).length)" "$T"
