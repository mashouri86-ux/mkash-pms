"""
Tamkeen — full system test in a real browser (Playwright).

What it checks, against a running Tamkeen server in test mode (Auth:Mode=Development):
  1. Branding: Tamkeen logo and title on the sign-in page and in the app.
  2. Data transfer: imports a migration/backup file, then compares every record in the system with the file.
  3. Every page opens for the PMO with no script errors.
  4. Workflows with separate signed-in people, each in their own browser:
       progress update → sector-lead approval · proof of completion upload/download ·
       change request PM → lead → PMO → NPC "yes" → change applied to the project ·
       project closure PM → lead accepts deliverables → Closed · KPI value by PMO ·
       executive view (no overdue, no work plan) · live updates between screens · status report download.

Usage:  python tests/e2e/full_system_test.py --base http://localhost:5173 --data tamkeen-migration.json \
            --owner owner@example.org --pm <pm email> --lead <lead email> --pmo <pmo email> --exec exec@example.org
The PM must be in the file's "directory" (as the manager of a project whose sector lead is --lead).
"""
import argparse, json, sys, time, os
from playwright.sync_api import sync_playwright

ap = argparse.ArgumentParser()
ap.add_argument("--base", default="http://localhost:5173")
ap.add_argument("--data", required=True)
ap.add_argument("--owner", default="owner@example.org")
ap.add_argument("--pm", required=True)
ap.add_argument("--lead", required=True)
ap.add_argument("--pmo", required=True)
ap.add_argument("--exec", dest="exec_", default="exec@example.org")
ap.add_argument("--shots", default="")
A = ap.parse_args()
B = A.base.rstrip("/")
DATA = json.load(open(A.data, encoding="utf-8"))
results = []


def check(name, ok, detail=""):
    results.append((name, bool(ok), detail))
    print(("✓ " if ok else "✗ ") + name + (f"  — {detail}" if detail and not ok else ""), flush=True)
    return ok


def shot(pg, name):
    if A.shots:
        os.makedirs(A.shots, exist_ok=True)
        pg.screenshot(path=os.path.join(A.shots, name + ".png"))


with sync_playwright() as p:
    br = p.chromium.launch()
    errors = {}

    def person(email, label):
        ctx = br.new_context(viewport={"width": 1440, "height": 900}, accept_downloads=True)
        pg = ctx.new_page()
        errors[label] = []
        pg.on("pageerror", lambda e: errors[label].append(str(e)))
        pg.goto(B + "/")
        pg.fill("input[name=email]", email)
        pg.click("button")
        pg.wait_for_load_state("networkidle")
        pg.wait_for_function("typeof S!=='undefined' && S.loaded", timeout=30000)
        pg.wait_for_timeout(800)
        return pg

    def api(pg, path):
        return pg.evaluate(f"fetch('{path}',{{credentials:'same-origin'}}).then(r=>r.json())")

    def wait_for(pg, js, secs=12):
        end = time.time() + secs
        while time.time() < end:
            if pg.evaluate(js):
                return True
            pg.wait_for_timeout(500)
        return False

    # ---------- 1. branding ----------
    anon = br.new_page()
    anon.goto(B + "/")
    check("Sign-in page shows the Tamkeen logo", anon.evaluate("[...document.images].some(i=>i.src.includes('tamkeen-logo')&&i.naturalWidth>0)"))
    check("Sign-in page title is Tamkeen", "Tamkeen" in anon.title(), anon.title())
    shot(anon, "01-signin")

    owner = person(A.owner, "owner")
    check("Owner signs in as PMO with switch-user", owner.evaluate("[S.isOwner,S.role]") == [True, "pmo"], str(owner.evaluate("[S.isOwner,S.role]")))
    check("App title and header logo are Tamkeen", "Tamkeen" in owner.title() and owner.evaluate("document.querySelector('.brand-logo').naturalWidth>0"))

    # ---------- 2. data transfer ----------
    owner.goto(B + "/system/import.html")
    owner.set_input_files("#f", A.data)
    owner.click("#go")
    owner.wait_for_function("document.getElementById('out').textContent.startsWith('Done') || document.getElementById('out').textContent.startsWith('Import failed')", timeout=60000)
    out = owner.inner_text("#out")
    check("Migration file imports", out.startswith("Done"), out)
    owner.click("#go")
    owner.wait_for_timeout(2500)
    again = owner.inner_text("#out")
    check("Importing the same file again changes nothing", "0 added, 0 updated" in again and again.count("0 added, 0 updated") == len(DATA["collections"]), again)

    mismatch = []
    for coll, items in DATA["collections"].items():
        got = {d["id"]: d["data"] for d in api(owner, f"/api/docs/{coll}")["docs"]}
        for it in items:
            want = {k: v for k, v in it.items() if k != "id"}
            if got.get(it["id"]) != want:
                mismatch.append(f"{coll}/{it['id']}")
        check(f"All {len(items)} {coll} records match the dashboard exactly", not [m for m in mismatch if m.startswith(coll + '/')], ", ".join(mismatch[:5]))

    owner.goto(B + "/")
    owner.wait_for_function("typeof S!=='undefined' && S.loaded", timeout=30000)
    owner.wait_for_timeout(1000)
    active = sum(1 for x in DATA["collections"]["projects"] if not x.get("archived"))
    budget = sum(x.get("budget") or 0 for x in DATA["collections"]["projects"] if not x.get("archived"))
    st = owner.evaluate("[S.projects.length,S.plans.length,S.access.length,S.directory.length,S.changes.length,S.projects.reduce((s,p)=>s+(+p.budget||0),0)]")
    check(f"Dashboard shows {active} active projects", st[0] == active, str(st))
    check("Dashboard shows every work plan, user, PM and change request", st[1:5] == [len(DATA["collections"][c]) for c in ("plans", "access", "directory", "changes")], str(st))
    check(f"Portfolio budget totals QAR {budget:,}", abs(st[5] - budget) < 1, str(st[5]))

    # ---------- 3. every page ----------
    views = owner.evaluate("[...document.querySelectorAll('#tabs a[data-view]')].map(a=>a.dataset.view)")
    for v in views:
        owner.evaluate(f"location.hash='#{v}'")
        owner.wait_for_timeout(600)
        shot(owner, f"10-{v}")
    check(f"All {len(views)} pages open without errors ({', '.join(views)})", len(views) >= 13 and not errors["owner"], "; ".join(errors["owner"][:3]))

    # ---------- people ----------
    owner.evaluate(f"""putDoc('access',{{id:'a-e2e-exec',email:'{A.exec_}',name:'Executive (test)',role:'executive',by:'e2e',updatedAt:nowIso()}})""")
    pm = person(A.pm, "pm")
    check("Project manager signs in straight into their projects", pm.evaluate("[S.role,S.myProjects.size>0]") == ["pm", True], str(pm.evaluate("[S.role,S.myProjects.size]")))
    pid = pm.evaluate("[...S.myProjects].map(projById).filter(Boolean).find(p=>(planOf(p.id)||{acts:[]}).acts.length>0).id")
    lead_name = pm.evaluate(f"projById('{pid}').sponsor")
    lead = person(A.lead, "lead")
    check("Sector lead signs in with their sector", lead.evaluate("S.role") == "lead" and lead.evaluate(f"leadMatch(projById('{pid}'),S.leadName)"), f"{lead.evaluate('[S.role,S.leadName]')} vs {lead_name}")
    pmo = person(A.pmo, "pmo")
    check("PMO user signs in as PMO", pmo.evaluate("S.role") == "pmo")
    ex = person(A.exec_, "exec")
    tabs = ex.evaluate("[...document.querySelectorAll('#tabs a[data-view]')].filter(a=>getComputedStyle(a).display!=='none').map(a=>a.dataset.view)")
    check("Executive gets the executive view (no work plan, no updates)", ex.evaluate("S.role") == "executive" and "workplan" not in tabs and "updates" not in tabs, str(tabs))
    ex.evaluate("location.hash='#exec'"); ex.wait_for_timeout(800)
    check("Executive Leadership page shows no overdue activities", "overdue" not in ex.inner_text("#view").lower())
    shot(ex, "20-executive-leadership")
    ex.evaluate("location.hash='#snap'"); ex.wait_for_timeout(800)
    snap = ex.evaluate("""(()=>{const n=[...document.querySelectorAll('.sn-h .sn-hn')].map(x=>+x.textContent.replace(/[^0-9]/g,''));return {hero:!!document.querySelector('.sn-hero'),tiles:document.querySelectorAll('.sn-tile').length,health:n.reduce((a,b)=>a+b,0),total:fProjects().length,pillars:document.querySelectorAll('.sn-p').length}})()""")
    check("Executive Snapshot: hero, 4 tiles, health adds up to all projects, pillar cards", snap["hero"] and snap["tiles"] == 4 and snap["health"] == snap["total"] and snap["pillars"] > 0, str(snap))
    shot(ex, "21-executive-snapshot")
    pmn = pm.evaluate("[...document.querySelectorAll('#tabs a[data-view=snap]')].filter(a=>getComputedStyle(a).display!=='none').length")
    check("Project managers don't get the Snapshot tab", pmn == 0)

    # ---------- 4a. progress update → approval ----------
    act = pm.evaluate(f"(()=>{{const a=planOf('{pid}').acts.find(a=>a.st!=='done');return a&&a.id}})()")
    pm.evaluate(f"""(async()=>{{const pl=planOf('{pid}');const before=JSON.parse(JSON.stringify(pl.acts));const acts=pl.acts.map(a=>a.id==='{act}'?{{...a,pr:55,st:'inProgress',n:'E2E progress note'}}:a);await savePlan('{pid}',acts,before);await submitUpdate('{pid}','E2E bi-weekly submission')}})()""")
    pm.wait_for_timeout(1500)
    pend = api(owner, "/api/docs/updates")["docs"]
    mine = [d for d in pend if d["data"].get("projectId") == pid]
    check("Progress update saved and waiting for approval", mine and all(d["data"].get("status") == "pending" for d in mine), str([d["data"].get("status") for d in mine]))
    check("Sector lead's screen shows it without reloading (live update)", wait_for(lead, f"S.updates.filter(u=>u.projectId==='{pid}'&&u.status==='pending').length>={len(mine)}"))
    lead.evaluate(f"apDecide(S.updates.filter(u=>u.projectId==='{pid}'&&u.status==='pending').map(u=>u.id),'approved')")
    lead.wait_for_timeout(1500)
    st = [d["data"].get("status") for d in api(owner, "/api/docs/updates")["docs"] if d["data"].get("projectId") == pid]
    check("Sector lead approves; status saved as approved", st and all(s == "approved" for s in st), str(st))
    who = [d["data"].get("reviewedBy") for d in api(owner, "/api/docs/updates")["docs"] if d["data"].get("projectId") == pid]
    check("The approval is recorded under the sector lead's name (not someone else's)", who and all(w == A.lead for w in who), str(who))
    plan = {d["id"]: d["data"] for d in api(owner, "/api/docs/plans")["docs"]}[pid]
    check("Work plan in the database has the new progress", any(a["id"] == act and a.get("pr") == 55 for a in plan["acts"]))

    # ---------- 4b. proof of completion ----------
    done = pm.evaluate(f"(()=>{{const a=planOf('{pid}').acts.find(a=>actState(a)==='done');return a&&a.id}})()")
    pm.evaluate(f"openDrawer('{pid}')"); pm.wait_for_timeout(500)
    pm.evaluate("S.dTab='evidence';S.evFilter='all';renderDrawer(true)"); pm.wait_for_timeout(500)
    inp = pm.locator(f'input.ev-input[data-aid="{done}"]') if pm.locator(f'input.ev-input[data-aid="{done}"]').count() else pm.locator("input.ev-input").first
    inp.set_input_files(files=[{"name": "proof-of-completion.pdf", "mimeType": "application/pdf", "buffer": b"%PDF-1.4 Tamkeen e2e proof"}])
    ok = wait_for(pm, f"S.evidence.some(e=>e.projectId==='{pid}'&&e.name==='proof-of-completion.pdf')", 15)
    shot(pm, "30-pm-evidence")
    ev = [d["data"] for d in api(owner, "/api/docs/evidence")["docs"] if d["data"].get("projectId") == pid]
    body = owner.evaluate(f"fetch('/_blob/{ev[0]['assetId']}').then(r=>r.text())") if ev else ""
    check("Project manager attaches proof of completion; file opens for others", ok and body == "%PDF-1.4 Tamkeen e2e proof", body[:40])
    evu = [d["data"] for d in api(owner, "/api/docs/updates")["docs"] if d["data"].get("projectId") == pid and d["data"].get("kind") == "evidence"]
    check("The approval item links straight to the attached file", evu and all(u.get("assetId") for u in evu), str([u.get("assetId") for u in evu]))
    aid_used = inp.get_attribute("data-aid")
    pm.evaluate("S.dTab='evidence';S.evFilter='all';renderDrawer(true)"); pm.wait_for_timeout(400)
    pm.locator(f'input.ev-input[data-aid="{aid_used}"]').set_input_files(files=[
        {"name": "meeting-minutes.docx", "mimeType": "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "buffer": b"PK\x03\x04 docx"},
        {"name": "budget-sheet.xlsx", "mimeType": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "buffer": b"PK\x03\x04 xlsx"}])
    three = wait_for(pm, f"S.evidence.filter(e=>e.projectId==='{pid}'&&e.actId==='{aid_used}').length===3", 15)
    pm.evaluate("renderDrawer(true)"); pm.wait_for_timeout(300)
    can_more = pm.locator(f'input.ev-input[data-aid="{aid_used}"]').count()
    if can_more:
        pm.locator(f'input.ev-input[data-aid="{aid_used}"]').set_input_files(files=[{"name": "extra.pdf", "mimeType": "application/pdf", "buffer": b"%PDF extra"}])
        pm.wait_for_timeout(2500)
    n_ev = pm.evaluate(f"S.evidence.filter(e=>e.projectId==='{pid}'&&e.actId==='{aid_used}').length")
    check("Word and Excel files attach; at most 3 files per activity", three and n_ev == 3, f"files={n_ev}")
    pm.evaluate("closeDrawer()")

    # ---------- 4c. change request → NPC → applied ----------
    pm.evaluate("location.hash='#changes';S.crOpen=null;S.crDraft=null;render()"); pm.wait_for_timeout(400)
    pm.click('#pageActions [data-act="cr-new"]'); pm.wait_for_timeout(400)
    if pm.locator("#frm select[name=pid]").count():  # several projects: pick one; one project: the form opens directly
        pm.select_option("#frm select[name=pid]", value=pid); pm.click("#frm button.primary")
    pm.wait_for_timeout(700)
    pm.select_option('select[data-crf="f.type"]', value="تغيير الميزانية المخطط لها للمشروع"); pm.wait_for_timeout(300)
    pm.fill('input[data-crf="ap.budget"]', "1234567")
    req, mgr = pm.evaluate(f"[S.crDraft.f.requester, projById('{pid}').manager]")
    check("Requester defaults to the project manager", req == mgr and bool(mgr), f"{req} vs {mgr}")
    opts = pm.evaluate("[...document.querySelectorAll('select[data-crf=\"f.requester\"] option')].map(o=>o.value).filter(Boolean)")
    check("Requester can be switched to a PMO team member", len(opts) >= 2, str(opts))
    pm.set_input_files('input.cr-att-in[data-k="charter"]', files=[{"name": "updated-charter.pdf", "mimeType": "application/pdf", "buffer": b"%PDF charter"}])
    wait_for(pm, "crAtt(S.crDraft,'charter').length===1", 15)
    pm.set_input_files('input.cr-att-in[data-k="template"]', files=[{"name": "change-request-filled.xlsm", "mimeType": "application/vnd.ms-excel.sheet.macroEnabled.12", "buffer": b"PK\x03\x04 xlsm"}])
    wait_for(pm, "crAtt(S.crDraft,'template').length===1", 15)
    if not pm.evaluate("S.crDraft.f.requester"):
        pm.fill('input[data-crf="f.requester"]', "E2E Project Manager")
    if not pm.evaluate("S.crDraft.f.current"):
        pm.fill('textarea[data-crf="f.current"]', "الميزانية الحالية")
    pm.fill('textarea[data-crf="f.proposed"]', "1,234,567"); pm.fill('textarea[data-crf="f.reasons"]', "اختبار شامل للنظام")
    pm.select_option('select[data-crf="f.impact.budget.on"]', value="نعم"); pm.wait_for_timeout(250)
    pm.fill('textarea[data-crf="f.impact.budget.d"]', "زيادة الميزانية")
    pm.click('[data-act="cr-submit"]'); pm.wait_for_timeout(500)
    if pm.evaluate("!!document.querySelector('#cfmBtn')"):
        pm.click("#cfmBtn"); pm.wait_for_timeout(800)
    if pm.evaluate("S.crDraft&&S.crDraft.status==='draft'"):
        print("   form message:", pm.evaluate("S.crErr||''"), pm.evaluate("[...document.querySelectorAll('.err')].map(e=>e.innerText).join(' | ')"))
    cid = pm.evaluate("S.crDraft&&S.crDraft.id")
    code = pm.evaluate("S.crDraft&&S.crDraft.code")
    shot(pm, "40-cr-submitted")
    crs = {d["id"]: d["data"] for d in api(owner, "/api/docs/changes")["docs"]}
    check("Change request submitted with an NDS3 code, waiting for the sector lead", cid in crs and crs[cid]["status"] == "lead" and bool(code), f"{code} {crs.get(cid,{}).get('status')}")
    att = (crs.get(cid) or {}).get("attachments") or {}
    check("Change request carries the updated charter (PDF) and the filled template (Excel)", len(att.get("charter", [])) == 1 and len(att.get("template", [])) == 1, json.dumps(att)[:120])
    wait_for(lead, f"(S.changes||[]).some(c=>c.id==='{cid}'&&c.status==='lead')")
    lead.evaluate(f"crOpen('{cid}')"); lead.wait_for_timeout(500)
    lead.click('[data-act="cr-ok"]'); lead.wait_for_timeout(1000)
    if lead.evaluate("!!document.querySelector('#cfmBtn')"):
        lead.click("#cfmBtn"); lead.wait_for_timeout(800)
    wait_for(pmo, f"(S.changes||[]).some(c=>c.id==='{cid}'&&c.status==='pmo')")
    pmo.evaluate(f"crOpen('{cid}')"); pmo.wait_for_timeout(500)
    pmo.click('[data-act="cr-ok"]'); pmo.wait_for_timeout(1000)
    if pmo.evaluate("!!document.querySelector('#cfmBtn')"):
        pmo.click("#cfmBtn"); pmo.wait_for_timeout(800)
    st = {d["id"]: d["data"] for d in api(owner, "/api/docs/changes")["docs"]}[cid]["status"]
    check("Sector lead then PMO approve; request goes to the NPC", st == "npc", st)
    pmo.evaluate("location.hash='#approvals'"); pmo.wait_for_timeout(800)
    shot(pmo, "41-pmo-npc-decision")
    pmo.click(f'.ap-item [data-act="cr-npc-yes"][data-id="{cid}"]'); pmo.wait_for_timeout(400)
    pmo.click("#cfmBtn"); pmo.wait_for_timeout(1500)
    crs = {d["id"]: d["data"] for d in api(owner, "/api/docs/changes")["docs"]}
    proj = {d["id"]: d["data"] for d in api(owner, "/api/docs/projects")["docs"]}[pid]
    check("NPC 'yes' approves the request and the new budget is applied to the project", crs[cid]["status"] == "approved" and proj.get("budget") == 1234567, f"{crs[cid]['status']} budget={proj.get('budget')}")
    check("Other screens see the new budget (live update)", wait_for(lead, f"+projById('{pid}').budget===1234567"))

    # ---------- 4d. KPI value ----------
    pmo.evaluate("location.hash='#kpis'"); pmo.wait_for_timeout(800)
    kid = pmo.evaluate("KPIS&&KPIS.strategic[0].id")
    pmo.evaluate(f"kpiEdit('{kid}')"); pmo.wait_for_timeout(400)
    pmo.fill("#frm [name=baseline]", "60"); pmo.fill("#frm [name=target]", "80"); pmo.fill("#frm [name=current]", "66")
    pmo.click("#frm button.primary"); pmo.wait_for_timeout(1200)
    k = {d["id"]: d["data"] for d in api(owner, "/api/docs/kpis")["docs"]}.get(kid, {})
    check("PMO records a KPI's baseline, target and current value", str(k.get("current")) in ("66", "66.0"), json.dumps(k, ensure_ascii=False)[:120])

    # ---------- 4e. closure ----------
    pm.evaluate(f"openDrawer('{pid}')"); pm.wait_for_timeout(500)
    pm.click('#drawer [data-act="cl-start"]'); pm.wait_for_timeout(500)
    pm.fill('textarea[data-clf="summary"]', "اختبار: تم تسليم المخرجات"); pm.fill('textarea[data-clf="lessons"]', "اختبار")
    pm.click('[data-act="cl-submit"]'); pm.wait_for_timeout(600)
    if pm.evaluate("!!document.querySelector('#cfmBtn')"):
        pm.click("#cfmBtn"); pm.wait_for_timeout(900)
    cl = [d["data"] for d in api(owner, "/api/docs/closures")["docs"] if d["data"].get("projectId") == pid]
    check("Project manager requests closure", cl and cl[-1].get("status") == "pending", str([c.get("status") for c in cl]))
    pm.evaluate("closeDrawer()")
    wait_for(lead, f"(S.closures||[]).some(c=>c.projectId==='{pid}'&&c.status==='pending')")
    lead.evaluate(f"openDrawer('{pid}')"); lead.wait_for_timeout(500)
    lead.evaluate("S.dTab='closure';renderDrawer(true)"); lead.wait_for_timeout(500)
    n = lead.evaluate("clOf(S.drawer).deliverables.length")
    for i in range(n):
        lead.click(f'[data-act="cl-acc"][data-i="{i}"][data-v="accepted"]'); lead.wait_for_timeout(250)
    shot(lead, "50-lead-closure")
    lead.click('[data-act="cl-ok"]'); lead.wait_for_timeout(600)
    if lead.evaluate("!!document.querySelector('#cfmBtn')"):
        lead.click("#cfmBtn"); lead.wait_for_timeout(900)
    proj = {d["id"]: d["data"] for d in api(owner, "/api/docs/projects")["docs"]}[pid]
    check(f"Sector lead accepts all {n} deliverables and closes the project", proj.get("status") == "closed", str(proj.get("status")))

    # ---------- 4f. report ----------
    owner.evaluate("location.hash='#overview'"); owner.wait_for_timeout(500)
    with owner.expect_download() as dl:
        owner.evaluate("downloadReport(rpRange('biweekly',0))")
    d = dl.value
    html = open(d.path(), encoding="utf-8").read()
    check("Bi-weekly status report downloads with the Tamkeen logo", d.suggested_filename.startswith("Tamkeen-biweekly-report") and "data:image/png;base64" in html, d.suggested_filename)

    # ---------- errors ----------
    all_err = {k: v for k, v in errors.items() if v}
    check("No script errors on any screen", not all_err, json.dumps(all_err)[:300])
    br.close()

passed = sum(1 for r in results if r[1])
print(f"\n{passed} passed, {len(results) - passed} failed")
sys.exit(0 if passed == len(results) else 1)
