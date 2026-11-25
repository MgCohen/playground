# N1 end to end: two orch processes ("local" and "cloud") share one Notion store. No local store folder.
# Usage: NOTION_TOKEN=... bash spikes/notion-store/e2e.sh <parent page id>
# The script makes a child page "E2E store <time>" under the parent page and uses it as the store.
set -u
cd "$(dirname "$0")/../.."
PARENT=${1:?parent page id}
ORCH="dotnet src/Orchestrator.Cli/bin/Debug/net10.0/orch.dll"
PAGE=$(curl -s -X POST -H "Authorization: Bearer $NOTION_TOKEN" -H "Notion-Version: 2022-06-28" -H "Content-Type: application/json" https://api.notion.com/v1/pages \
  -d '{"parent":{"page_id":"'"$PARENT"'"},"properties":{"title":{"title":[{"text":{"content":"E2E store '"$(date -u +%H:%M)"'"}}]}}}' \
  | python3 -c "import json,sys;print(json.load(sys.stdin)['id'])")
export ORCH_STORE="notion:$PAGE" ORCH_HOME=$PWD
echo "store: $ORCH_STORE"
t() { local s=$(date +%s.%N); "$@"; echo "  [$(python3 -c "print(round($(date +%s.%N)-$s,1))") s] $*" | sed "s#$ORCH#orch#"; }
t $ORCH init
t $ORCH enable --title "N1 local session" --id n1-local
t $ORCH enable --title "N1 cloud session" --id n1-cloud
t $ORCH post question --from session:n1-local --to user --needs answer --thread n1-t1 --json templates/question/example.json
t $ORCH questions
t $ORCH answer n1-t1 "BLUE: from the cloud session"
t $ORCH events --session n1-local
t $ORCH tree
t $ORCH events --view thread n1-t1
t $ORCH questions
