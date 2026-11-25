# Tamkeen · تمكين

<p align="center"><img src="web/data/brand/tamkeen-logo.png" alt="Tamkeen" height="80"> &nbsp; <img src="web/data/brand/ministry-logo.png" alt="Ministry of Social Development and Family" height="80"></p>

Tamkeen (تمكين) is the project management system of the Ministry of Social Development and Family. People use exactly the screens and features of the Tamkeen dashboard. The system adds what the dashboard couldn't have on its own:
- its own server and database, SQL Server, hosted in-house;
- sign-in with ministry accounts through Microsoft Entra ID;
- a file store for proof of completion, project charters and closure attachments;
- live updates between everyone who has it open.

| Part | What it is | Folder |
|---|---|---|
| Screens | The dashboard, served by the system. It has every page: Home (strategy roadmap), Overview, Leadership, KPIs, Projects, Work plan, Progress updates, Approvals, Change requests, Risks, Timeline, Data and Users. | `web/index.html`, `web/data` |
| System data layer | Connects the screens to the server: records, files, sign-in and downloads | `web/system/claude-shim.js` |
| Server | ASP.NET Core 10 (LTS, supported to November 2028). It handles sign-in, the record store, file uploads and live updates. | `src/MKAsh.Api` |
| Business rules library | Progress, weighting, status, KPIs, reporting periods and permissions as .NET code, tested against the dashboard's figures | `src/MKAsh.Core`, `tests` |
| Starting data | The 50 PQI projects and their work plans | `database/seed/docs` |

## Try it in your browser (GitHub Codespaces)

1. On the repository page, click **Code → Codespaces → Create codespace on main**.
2. Wait 2–4 minutes on the first start. The app opens on port **5173**. If it doesn't, use the **Ports** tab and open port 5173.
3. Sign in with your email. On the test system there is no password.
   - **The first person to sign in becomes the owner:** PMO rights, plus **⇄ Switch user** to see the system as an executive, a sector lead or a project manager.
   - Other people get the role the PMO gives them on the **Users** page.
4. Stop the codespace when you're done (Codespaces → … → Stop) to save free hours.

The log is at `/tmp/mkash.log`. To restart, run `bash scripts/dev-start.sh`.

## Moving data from the dashboard

Open **/system/import.html** while signed in as PMO, choose the migration file (`tamkeen-migration.json`) and click **Import**. Records are matched by id, so importing twice changes nothing. The same page downloads a full backup.

The migration file holds staff emails, so it is kept out of Git and handed over separately.

## Run it on a server

See [docs/setup.md](docs/setup.md) for the database, Entra ID sign-in and hosting.

## Checks

GitHub Actions runs the following on every push (`.github/workflows/ci.yml`):
- the .NET build;
- the business-rule tests: progress, planned % and status for all 50 projects and every strategic KPI, compared with the dashboard's figures;
- a check that the dashboard script compiles;
- an end-to-end run on SQL Server and on SQLite: sign-in, permissions, records, live updates and file upload/download;
- a full system test in a real browser (`tests/e2e/full_system_test.py`). It covers branding, the data transfer and a record-by-record comparison, every page, and the workflows with separate signed-in people:
  - progress update → sector-lead approval;
  - proof of completion;
  - change request PM → lead → PMO → NPC "yes" → change applied;
  - KPI values;
  - closure → Closed;
  - the executive view, live updates and report download.

## Documentation
- [Architecture](docs/architecture.md)
- [Roles and permissions](docs/roles.md)
- [Business rules](docs/business-rules.md)
- [Setup and deployment](docs/setup.md)
