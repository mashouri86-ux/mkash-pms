# Architecture

```
Browser (React, EN/AR)
   │  Entra ID token (production) or X-Dev-User header (development only)
   ▼
MKAsh.Api  (ASP.NET Core 8)
   ├─ Controllers: me/strategy, projects, approvals, change-requests, closure, kpis, leadership, reports, admin
   ├─ Services: CurrentUser (account → role), PortfolioService (figures), ChangeRequestService (codes, apply, Excel)
   └─ EF Core → SQL Server
MKAsh.Core (no dependencies)
   └─ Rules: ProgressRules, WeightTemplates, KpiRules, ReportingPeriods, AccessRules, WorkflowRules
```

**One place for every rule.** All figures (progress, planned %, variance, status, weighted progress, KPI delivery) come from `MKAsh.Core`. Every page calls `PortfolioService`, so the numbers on the leadership page, project list, reports and KPIs always agree. `MKAsh.Core` has no database or web code, which is why the golden tests can run it alone.

**Permissions are enforced by the API.** The web app builds its menu from `/api/me` (`pages`), but every endpoint checks `AccessRules` again. Hiding a tab is never the only protection.

**Live vs approved values.** Each activity stores the manager's live progress and the last approved progress. Managers and the PMO work on live values. Leadership pages and executives see approved values only, until the sector lead or PMO approves an update.

## Data model (main tables)

| Table | Holds |
|---|---|
| Portfolios, Initiatives | The 6 sector outcomes and their initiatives (from the strategy roadmap) |
| People | Everyone who can sign in: email, role, and for sector leads the lead name used on projects |
| Projects | Portfolio projects: lead, manager, budget, dates, status override (On hold / Closed …), type |
| WorkPackages, Activities | The work plan; activities have live and approved progress/status |
| Updates | Every progress change and its approval: the audit trail and approvals queue |
| ChangeRequests (+ WorkflowEvents) | NDS3 change requests: form sections 1–3, apply values, NPC sections 4–6, history |
| Closures, ClosureDeliverables | Closure requests and deliverable acceptance |
| Kpis, KpiLinks | Strategic and Ministry KPIs and the projects that deliver them (impact 3/2/1) |
| Risks, Milestones, Evidence | Per-project registers |
| WeightTemplates, Settings | Phase weights per project type; update cycle; progress view |

On first start the API creates the database. Once the team adds EF migrations (`dotnet ef migrations add Initial`), it applies them instead. The API then loads `database/seed` when `Seed:OnStartup` is true and the database is empty.

## Change requests and the Excel template

`ChangeRequestService.WorkbookAsync` opens `templates/Change_Request_Template.xlsm` and writes the request into sheet 4, using the same cells as the prototype. It sets Excel to recalculate on open and returns the file with its macros intact. That is the file the NPC asked for.
