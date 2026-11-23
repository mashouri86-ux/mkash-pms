"""Builds a migration file for the automatic checks: the real portfolio (seed) plus test people at example.org."""
import json, sys
seed = json.load(open("database/seed/docs/seed.json", encoding="utf-8"))
data = {
    "source": "Tamkeen automatic checks",
    "collections": {
        "projects": seed["projects"],
        "plans": seed["plans"],
        "access": [
            {"id": "a-t-pmo", "email": "pmo@example.org", "name": "PMO (test)", "role": "pmo"},
            {"id": "a-t-lead", "email": "lead@example.org", "name": "Sector lead (test)", "role": "lead", "lead": "الدكتورة شريفة العمادي"},
        ],
        # Project manager of 1.1.1 (sector lead: الدكتورة شريفة العمادي)
        "directory": [{"id": "d-t-pm", "email": "pm@example.org", "name": "مريم المالكي"}],
        "changes": [],
    },
}
json.dump(data, open(sys.argv[1], "w", encoding="utf-8"), ensure_ascii=False)
print("wrote", sys.argv[1])
