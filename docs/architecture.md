# Architecture

```
Browser
  web/index.html ─ the dashboard (all pages, charts, workflows, Excel/PDF exports)
  web/system/claude-shim.js ─ the system data layer
        │  same-origin requests with the sign-in cookie
        ▼
MKAsh.Api (ASP.NET Core 8)
  /signin, /signout ............ Entra ID (production) or the test form (development)
  /api/me, /api/profiles ....... the signed-in account, owner/PMO flags, names
  /api/docs/{collection} ....... list / save / delete records
  /api/sync?since= ............ everything changed since a point in time (live updates, every 4 s)
  /api/assets, /_blob/{id} ..... upload / open files (proof of completion, charters, closure files)
  /api/import, /api/export ..... data transfer from the dashboard; full backup (PMO only)
  static files ................. the dashboard and its data files (kpis.json, roadmap pages, fonts, CR template)
        │
        ▼
SQL Server: Docs (Collection, Id, Json, Version, Deleted, UpdatedBy, UpdatedAt) · Assets (file bytes)
```

## Why the screens are the dashboard itself

The dashboard already holds every page and workflow the ministry agreed on: progress updates and approvals, change requests through the NPC, closure, KPIs, reports, imports and users. Rewriting them would risk losing features. Instead, the system keeps the screens as they are and replaces only the layer under them: where data is stored, who is signed in, where files go.

The dashboard works with these record collections, and each one is stored in the `Docs` table:
`projects, plans, risks, milestones, evidence, updates, changes, kpis, closures, roles, access, directory, settings`.

## Permissions

| Who | Can |
|---|---|
| Not signed in | Nothing: every page, data call and file requires sign-in |
| Owner (`Pms:Owners`, or on a test system the first person to sign in) | Everything, plus switching user to preview other roles |
| PMO (an `access`/`roles` record with role `pmo`) | Change users, roles, settings and KPI values (enforced by the server) |
| Signed-in users | Read the portfolio. Save progress updates, risks, milestones, evidence, change requests and closures. The screens then limit each role to its own projects, as in the dashboard. |

**Next hardening step:** move the per-project limits (managers edit only their projects, leads approve only their sector) from the screens into the server, one collection at a time. The rules for this are already written and tested in `MKAsh.Core/Rules/AccessRules.cs`.

## Live updates
Every save stamps the record with a version (UTC ticks). Each open screen asks `/api/sync` every 4 seconds for anything newer, so approvals, comments and uploads appear for everyone without a reload. Deleted records are kept as tombstones so other screens remove them too.
