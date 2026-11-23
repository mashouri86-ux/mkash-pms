#!/usr/bin/env bash
# End-to-end check against a running MKAsh PMS in Development mode (test sign-in, seeded data).
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
check "Dashboard page is served with the system data layer" 'as $owner $BASE/ | grep -q "system/claude-shim.js"'
check "Dashboard data files are served" '[ "$(code $owner $BASE/data/kpis.json)" = 200 ]'
check "57 projects loaded (50 active + archived/merged)" '[ "$(as $owner $BASE/api/docs/projects | jq ".docs | length")" = 57 ]'
check "55 work plans loaded" '[ "$(as $owner $BASE/api/docs/plans | jq ".docs | length")" = 55 ]'
check "No PMO officer field in projects" '[ "$(as $owner $BASE/api/docs/projects | jq "[.docs[].data | select(has(\"pmoFocal\"))] | length")" = 0 ]'

T0=$(as $owner $BASE/api/docs/risks | jq .now)
check "Anyone signed in can add a risk" '[ "$(code $other -X PUT -H "Content-Type: application/json" -d "{\"projectId\":\"x\",\"title\":\"Test risk\"}" $BASE/api/docs/risks/r-smoke)" = 204 ]'
check "Live updates report the new risk" '[ "$(as $owner "$BASE/api/docs/changes?since=$T0" | jq "[.changes[] | select(.id==\"r-smoke\")] | length")" -ge 1 ]'
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
check "Deleting a risk is reported to other screens" '[ "$(code $other -X DELETE $BASE/api/docs/risks/r-smoke)" = 204 ] && [ "$(as $owner "$BASE/api/docs/changes?since=$T0" | jq -r "[.changes[] | select(.id==\"r-smoke\")][-1].deleted")" = true ]'

echo "$pass passed, $fail failed"
[ "$fail" = 0 ]
