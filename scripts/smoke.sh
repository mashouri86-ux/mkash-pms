#!/usr/bin/env bash
# End-to-end check against a running API in Development mode (demo users, seeded data).
set -euo pipefail
API=${API:-http://localhost:5080/api}
pass=0
check() { echo "✓ $1"; pass=$((pass+1)); }
get() { curl -sf -H "X-Dev-User: $1" "$API$2"; }
code() { curl -s -o /dev/null -w "%{http_code}" -H "X-Dev-User: $1" "$API$2"; }

for i in $(seq 1 60); do curl -sf "$API/health" >/dev/null && break; sleep 2; done
curl -sf "$API/health" >/dev/null && check "API is up"

[ "$(get pmo@example.org /me | jq -r .role)" = "pmo" ] && check "PMO signs in"
n=$(get pmo@example.org /projects | jq length); [ "$n" = "50" ] && check "PMO sees all 50 projects"
n=$(get lead.attiyah@example.org /projects | jq length); [ "$n" = "13" ] && check "Sector lead Maha Al Attiyah sees her 13 projects"
[ "$(get executive@example.org /projects | jq '[.[].overdue] | map(select(. != null)) | length')" = "0" ] && check "Executive gets no overdue counts"
[ "$(code lead.attiyah@example.org /kpis)" = "403" ] && check "Sector leads cannot open KPIs"
[ "$(get executive@example.org '/kpis?category=strategic' | jq length)" = "19" ] && check "Executive sees 19 strategic KPIs"
[ "$(get executive@example.org '/leadership?lead=%D9%81%D9%87%D8%AF%20%D8%A7%D9%84%D8%AE%D9%8A%D8%A7%D8%B1%D9%8A%D9%86' | jq .projects)" = "11" ] && check "Leadership filter by sector lead (Fahad Al Khayarin: 11)"
for f in biweekly monthly quarterly yearly; do
  [ "$(get pmo@example.org "/reports?frequency=$f" | jq -r .period.frequency)" = "$f" ] && check "Report: $f"
done
[ "$(code nobody@example.org /me)" = "403" ] && check "Unregistered accounts are refused"

# Change request: PMO drafts, submits (goes to NPC), NPC approves → budget applied
pid=$(get pmo@example.org /projects | jq '.[0].id')
form='{"date":"2026-11-22","requester":"PMO","initCode":"SD.1.1","initName":"x","ownerEntity":"'"$(jq -r '.entities[] | select(.[2]=="MSDF") | .[0]' database/seed/cr-lists.json)"'","level":"مشروع","type":"تغيير الميزانية المخطط لها للمشروع","projCode":"SD.1.1.1","projName":"x","projOwner":"x","linked":"لا","current":"1","proposed":"2","reasons":"r"}'
id=$(curl -sf -H "X-Dev-User: pmo@example.org" -H "Content-Type: application/json" -d "{\"projectId\":$pid,\"form\":$form,\"apply\":{\"budget\":\"1234567\"}}" "$API/change-requests" | jq .id)
res=$(curl -sf -X POST -H "X-Dev-User: pmo@example.org" -H "Content-Type: application/json" -d '{}' "$API/change-requests/$id/submit")
[ "$(echo "$res" | jq -r .status)" = "withNpc" ] && check "Change request submitted with code $(echo "$res" | jq -r .code)"
curl -sf -H "X-Dev-User: pmo@example.org" -o /tmp/cr.xlsm "$API/change-requests/$id/excel" && unzip -l /tmp/cr.xlsm | grep -q vbaProject.bin && check "Excel export keeps the NDS3 macros"
npc=$(curl -sf -X POST -H "X-Dev-User: pmo@example.org" -H "Content-Type: application/json" -d '{"decision":"approved"}' "$API/change-requests/$id/npc")
echo "  NPC decision: $npc"
[ "$(get pmo@example.org /projects | jq ".[] | select(.id==$pid) | .budget == 1234567")" = "true" ] && check "NPC approval applied the new budget"

echo "$pass checks passed"
[ "$pass" -ge 16 ]
