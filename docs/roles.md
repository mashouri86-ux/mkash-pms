# Roles and permissions

Each person has exactly one role. The API checks every request (`src/MKAsh.Core/Rules/AccessRules.cs`).

| Page | PMO | Executive | Sector lead | Project manager |
|---|:-:|:-:|:-:|:-:|
| Home (strategy roadmap) | ✓ | ✓ | ✓ | ✓ |
| Leadership | ✓ | ✓ | ✓ | |
| KPIs | ✓ | ✓ | | |
| Projects | ✓ (all) | ✓ (all, approved figures) | ✓ (their sector) | ✓ (their projects) |
| Approvals | ✓ | | ✓ | ✓ (status of their own) |
| Change requests | ✓ | | | ✓ |
| Reports | ✓ | ✓ | ✓ | ✓ |
| Administration | ✓ | | | |

**Executives** never see overdue-activity counts or states, and always see approved figures.

**Sector leads** are linked to projects by the lead name on the project (for example `مها العطية`). They approve their projects' updates, change requests (first step) and closures. When a project's lead has no account, the PMO approves instead.

**Project managers** edit only the projects assigned to them. Their changes wait for approval, while PMO changes are approved immediately.

## Workflows

**Progress update:** manager changes an activity → *Pending* → sector lead or PMO approves (approved values updated) or returns (live value reset to the approved value).

**Change request (NDS3):** manager drafts → submit (code generated as `Type.Initiative.Entity.DDMMYY`) → sector lead → PMO → *With NPC* → PMO records the NPC decision:
- **Approved** applies the change to the project (name, description, manager, initiative, budget, dates, new project, or archive).
- **Rejected** leaves the project unchanged.
- **More information** returns the request to the manager.

**Project closure:** manager requests closure with a summary (one deliverable per work package) → sector lead accepts each deliverable → approve. The project becomes **Closed** and is locked from editing.
