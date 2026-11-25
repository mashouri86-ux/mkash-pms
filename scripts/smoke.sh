#!/usr/bin/env bash
# End-to-end check against a running Tamkeen in Development mode (test sign-in, seeded data).
set -uo pipefail
BASE=${BASE:-http://localhost:5173}
pass=0; fail=0
ok()  { echo "✓ $1"; pass=$((pass+1)); }
bad() { echo "✗ $1"; fail=$((fail+1)); }
check() { if eval "$2"; then ok "$1"; else bad "$1"; fi; }
J=/tmp/smoke; mkdir -p $J; rm -f $J/*
owner=$J/owner.txt; other=$J/other.txt
signin() { curl -s -o /dev/null -c "$2" -d "email=$1" -d "returnUrl=/" "$BASE/signin"; }
as() { local jar=$1; shift; curl -s -b "$jar" "$@"; }
code() { local jar=$1; shift; curl -s -o /dev/null -w "%{http_code}" -b "$jar" "$@"; }

for i in $(seq 1 60); do curl -sf "$BASE/api/health" >/dev/null && break; sleep 2; done
check "Server is up" '[ "$(curl -s -o /dev/null -w %{http_code} $BASE/api/health)" = 200 ]'
check "Pages need sign-in (redirect to /signin)" '[ "$(curl -s -o /dev/null -w %{redirect_url} $BASE/)" != "" ]'
check "Data needs sign-in (401)" '[ "$(curl -s -o /dev/null -w %{http_code} $BASE/api/docs/projects)" = 401 ]'

signin owner@example.org $owner
signin someone@example.org $other
check "First person to sign in is the owner" '[ "$(as $owner $BASE/api/me | jq -r .isOwner)" = true ]'
check "Second person is not" '[ "$(as $other $BASE/api/me | jq -r .isOwner)" = false ]'
check "Dashboard page is served with the system data layer" 'as $owner $BASE/ > $J/index.html && grep -q "system/claude-shim.js" $J/index.html'
check "Dashboard data files are served" '[ "$(code $owner $BASE/data/kpis.json)" = 200 ]'
check "Change requests list loads (not confused with live updates)" '[ "$(as $owner $BASE/api/docs/changes | jq ".docs | type")" = "\"array\"" ]'
check "57 projects loaded (50 active + archived/merged)" '[ "$(as $owner $BASE/api/docs/projects | jq ".docs | length")" = 57 ]'
check "55 work plans loaded" '[ "$(as $owner $BASE/api/docs/plans | jq ".docs | length")" = 55 ]'
check "No PMO officer field in projects" '[ "$(as $owner $BASE/api/docs/projects | jq "[.docs[].data | select(has(\"pmoFocal\"))] | length")" = 0 ]'

T0=$(as $owner $BASE/api/docs/risks | jq .now)
check "Someone not added to Tamkeen gets no data (403)" '[ "$(code $other $BASE/api/docs/projects)" = 403 ]'
check "...and is told they are not registered" '[ "$(as $other $BASE/api/me | jq -r .registered)" = false ]'
check "Owner adds them to the directory" '[ "$(code $owner -X PUT -H "Content-Type: application/json" -d "{\"email\":\"Someone@Example.org\",\"name\":\"Someone\"}" $BASE/api/docs/directory/d-someone)" = 204 ]'
check "Once added, they get data" '[ "$(code $other $BASE/api/docs/projects)" = 200 ] && [ "$(as $other $BASE/api/me | jq -r .registered)" = true ]'
check "A registered user who manages no project can't add a risk to one" '[ "$(code $other -X PUT -H "Content-Type: application/json" -d "{\"projectId\":\"x\",\"title\":\"Test risk\"}" $BASE/api/docs/risks/r-smoke)" = 403 ]'
check "Owner (PMO) can add a risk" '[ "$(code $owner -X PUT -H "Content-Type: application/json" -d "{\"projectId\":\"x\",\"title\":\"Test risk\"}" $BASE/api/docs/risks/r-smoke)" = 204 ]'
check "Live updates report the new risk" '[ "$(as $owner "$BASE/api/sync?since=$T0" | jq "[.changes[] | select(.id==\"r-smoke\")] | length")" -ge 1 ]'
check "Non-PMO cannot change the access list" '[ "$(code $other -X PUT -H "Content-Type: application/json" -d "{\"email\":\"x@y.z\",\"role\":\"pmo\"}" $BASE/api/docs/access/a-x)" = 403 ]'
check "Owner can add a PMO user" '[ "$(code $owner -X PUT -H "Content-Type: application/json" -d "{\"email\":\"someone@example.org\",\"name\":\"Someone\",\"role\":\"pmo\"}" $BASE/api/docs/access/a-someone)" = 204 ]'
check "That PMO user can now edit KPI values" '[ "$(code $other -X PUT -H "Content-Type: application/json" -d "{\"current\":5}" $BASE/api/docs/kpis/SD.1.K.1)" = 204 ]'
check "Names resolve from the access list" '[ "$(as $owner -X POST -H "Content-Type: application/json" -d "{\"ids\":[\"someone@example.org\"]}" $BASE/api/profiles | jq -r ".[\"someone@example.org\"].name")" = Someone ]'

printf '%%PDF-1.4 smoke test file' > $J/proof.pdf
up=$(as $other -F "file=@$J/proof.pdf;type=application/pdf" -F "type=application/pdf" $BASE/api/assets)
id=$(echo "$up" | jq -r .id)
check "Proof of completion uploads" '[ -n "$id" ] && [ "$id" != null ]'
check "Uploaded file downloads unchanged" 'as $owner $BASE/_blob/$id | cmp -s - $J/proof.pdf'
check "Unsupported file types are refused" '[ "$(code $other -F "file=@$J/proof.pdf;filename=x.exe" $BASE/api/assets)" = 400 ]'

# ---------------- governance rules (approved Nov 2026) ----------------
pm=$J/pm.txt; lead=$J/lead.txt; cover=$J/cover.txt
H='Content-Type: application/json'
put() { local jar=$1 path=$2 body=$3; code $jar -X PUT -H "$H" --data-binary "$body" "$BASE/api/docs/$path"; }
PROJ=$(as $owner $BASE/api/docs/projects)
P1=$(echo "$PROJ" | jq -r '.docs[] | select(.data.code=="1.1.1") | .id')
P2=$(echo "$PROJ" | jq -r '.docs[] | select(.data.code=="1.1.3") | .id')
put $owner directory/d-pm '{"email":"pm@example.org","name":"مريم المالكي"}' >/dev/null
put $owner access/a-lead '{"email":"lead@example.org","name":"Lead","role":"lead","lead":"الدكتورة شريفة العمادي"}' >/dev/null
put $owner directory/d-cover '{"email":"cover@example.org","name":"Cover Person"}' >/dev/null
signin pm@example.org $pm; signin lead@example.org $lead; signin cover@example.org $cover
check "6. A project manager can change their own project" '[ "$(put $pm risks/r-pm1 "{\"projectId\":\"$P1\",\"title\":\"PM risk\",\"probability\":2,\"impact\":2}")" = 204 ]'
check "6. ...but not someone else's project (403)" '[ "$(put $pm risks/r-pm2 "{\"projectId\":\"$P2\",\"title\":\"Not mine\"}")" = 403 ]'
check "6. A sector lead can change a project in their sector" '[ "$(put $lead risks/r-ld1 "{\"projectId\":\"$P1\",\"title\":\"Lead risk\",\"probability\":1,\"impact\":1}")" = 204 ]'
check "6. ...but not a project in another sector (403)" '[ "$(put $lead risks/r-ld2 "{\"projectId\":\"$P2\",\"title\":\"Other sector\"}")" = 403 ]'
check "6. A project manager can't create a new project (403)" '[ "$(put $pm projects/p-new "{\"code\":\"9.9.9\",\"name\":\"x\"}")" = 403 ]'
check "10. A critical risk without owner, mitigation and due date is refused (400)" '[ "$(put $pm risks/r-crit "{\"projectId\":\"$P1\",\"title\":\"Critical\",\"probability\":5,\"impact\":4}")" = 400 ]'
check "10. ...and accepted once they are filled in" '[ "$(put $pm risks/r-crit "{\"projectId\":\"$P1\",\"title\":\"Critical\",\"probability\":5,\"impact\":4,\"owner\":\"PM\",\"mitigation\":\"Weekly review\",\"due\":\"2026-12-31\"}")" = 204 ]'
check "7. A new update can't arrive already approved (403)" '[ "$(put $owner updates/u-auto "{\"projectId\":\"$P1\",\"by\":\"owner@example.org\",\"status\":\"approved\"}")" = 403 ]'
check "7. A project manager's update waits for approval" '[ "$(put $pm updates/u-1 "{\"projectId\":\"$P1\",\"by\":\"pm@example.org\",\"at\":\"2026-11-25T08:00:00Z\",\"status\":\"pending\"}")" = 204 ]'
check "7. They can't approve their own update (403)" '[ "$(put $pm updates/u-1 "{\"projectId\":\"$P1\",\"by\":\"pm@example.org\",\"at\":\"2026-11-25T08:00:00Z\",\"status\":\"approved\"}")" = 403 ]'
check "7. The sector lead can approve it" '[ "$(put $lead updates/u-1 "{\"projectId\":\"$P1\",\"by\":\"pm@example.org\",\"at\":\"2026-11-25T08:00:00Z\",\"status\":\"approved\",\"reviewedBy\":\"lead@example.org\"}")" = 204 ]'
check "7. A change request can't be approved by the person who raised it (403)" '[ "$(put $owner changes/c-1 "{\"projectId\":\"$P1\",\"createdBy\":\"owner@example.org\",\"status\":\"lead\"}")" = 204 ] && [ "$(put $owner changes/c-1 "{\"projectId\":\"$P1\",\"createdBy\":\"owner@example.org\",\"status\":\"pmo\"}")" = 403 ]'
as $owner $BASE/api/docs/plans | jq ".docs[] | select(.id==\"$P1\") | .data" > $J/plan.json
jq '. + {baseline:{at:"2026-11-25T08:00:00Z",by:"owner@example.org",acts:{}}}' $J/plan.json > $J/plan-bl.json
check "8. The PMO can lock a baseline" '[ "$(put $owner plans/$P1 "$(cat $J/plan-bl.json)")" = 204 ]'
check "8. After that the project manager can't move planned dates (403)" '[ "$(put $pm plans/$P1 "$(jq ".acts[0].pe=\"2031-01-01\"" $J/plan-bl.json)")" = 403 ]'
check "8. ...or remove an activity (403)" '[ "$(put $pm plans/$P1 "$(jq "del(.acts[0])" $J/plan-bl.json)")" = 403 ]'
check "8. ...or change the baseline itself (403)" '[ "$(put $pm plans/$P1 "$(jq "del(.baseline)" $J/plan-bl.json)")" = 403 ]'
check "8. ...but can still update progress" '[ "$(put $pm plans/$P1 "$(jq ".acts[0].pr=7" $J/plan-bl.json)")" = 204 ]'
check "3. The PMO publishes a cycle" '[ "$(put $owner cycles/2026-smoke "{\"period\":\"2026-smoke\",\"label\":\"Smoke\"}")" = 204 ]'
check "3. A published cycle is frozen (403)" '[ "$(put $owner cycles/2026-smoke "{\"period\":\"2026-smoke\",\"label\":\"Changed\"}")" = 403 ]'
check "3. Submissions for a published cycle are refused (403)" '[ "$(put $pm plans/$P1 "$(jq ".acts[0].pr=7 | .submitted={period:\"2026-smoke\",at:\"2026-11-25T09:00:00Z\",by:\"pm@example.org\"}" $J/plan-bl.json)")" = 403 ]'
check "11. Someone not covering can't change the project (403)" '[ "$(put $cover risks/r-cv "{\"projectId\":\"$P1\",\"title\":\"Cover\"}")" = 403 ]'
check "11. A project manager can't hand their duties to someone (403)" '[ "$(put $pm delegations/dl-pm "{\"kind\":\"pm\",\"from\":\"pm@example.org\",\"to\":\"cover@example.org\",\"projects\":[\"$P1\"],\"start\":\"2020-01-01\",\"end\":\"2099-12-31\"}")" = 403 ]'
check "11. The PMO delegates the project manager's duties" '[ "$(put $owner delegations/dl-pm "{\"kind\":\"pm\",\"from\":\"pm@example.org\",\"to\":\"cover@example.org\",\"projects\":[\"$P1\"],\"start\":\"2020-01-01\",\"end\":\"2099-12-31\"}")" = 204 ]'
check "11. ...and the delegate can now change the project" '[ "$(put $cover risks/r-cv "{\"projectId\":\"$P1\",\"title\":\"Cover\"}")" = 204 ]'
check "11. A sector lead can delegate their own approvals" '[ "$(put $lead delegations/dl-ld "{\"kind\":\"lead\",\"from\":\"lead@example.org\",\"to\":\"cover@example.org\",\"lead\":\"الدكتورة شريفة العمادي\",\"start\":\"2020-01-01\",\"end\":\"2099-12-31\"}")" = 204 ]'
check "9. The PMO names a KPI owner" '[ "$(put $owner kpis/K-SMOKE "{\"target\":10,\"current\":1,\"ownerEmail\":\"pm@example.org\",\"enteredBy\":\"owner@example.org\",\"signoff\":{\"status\":\"pending\"}}")" = 204 ]'
check "9. The KPI owner can update its current value" '[ "$(put $pm kpis/K-SMOKE "{\"target\":10,\"current\":2,\"ownerEmail\":\"pm@example.org\",\"enteredBy\":\"owner@example.org\",\"signoff\":{\"status\":\"pending\"}}")" = 204 ]'
check "9. ...but not its target (403)" '[ "$(put $pm kpis/K-SMOKE "{\"target\":20,\"current\":2,\"ownerEmail\":\"pm@example.org\",\"enteredBy\":\"owner@example.org\",\"signoff\":{\"status\":\"pending\"}}")" = 403 ]'
check "7/9. Whoever entered a baseline can't sign it off (403)" '[ "$(put $owner kpis/K-SMOKE "{\"target\":10,\"current\":2,\"ownerEmail\":\"pm@example.org\",\"enteredBy\":\"owner@example.org\",\"signoff\":{\"status\":\"approved\",\"by\":\"owner@example.org\"}}")" = 403 ]'
check "7/9. Another PMO member can" '[ "$(put $other kpis/K-SMOKE "{\"target\":10,\"current\":2,\"ownerEmail\":\"pm@example.org\",\"enteredBy\":\"owner@example.org\",\"signoff\":{\"status\":\"approved\",\"by\":\"someone@example.org\"}}")" = 204 ]'
check "Deleting a risk is reported to other screens" '[ "$(code $other -X DELETE $BASE/api/docs/risks/r-smoke)" = 204 ] && [ "$(as $owner "$BASE/api/sync?since=$T0" | jq -r "[.changes[] | select(.id==\"r-smoke\")][-1].deleted")" = true ]'

echo "$pass passed, $fail failed"
[ "$fail" = 0 ]
