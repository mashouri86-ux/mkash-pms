# Roles and permissions

Each person has exactly one role, set on the **Users** page. The server enforces sign-in and PMO-only changes (users, roles, settings, KPI values); the screens show each role only its own pages and projects. See [architecture.md](architecture.md#permissions).

| Page | PMO | Executive | Sector lead | Project manager |
|---|:-:|:-:|:-:|:-:|
| Home (strategy roadmap) | ✓ | ✓ | ✓ | ✓ |
| Overview | ✓ | | ✓ | ✓ |
| Leadership | ✓ | ✓ | ✓ | |
| KPIs | ✓ | ✓ | | |
| Projects | ✓ (all) | ✓ (all) | ✓ (their sector) | ✓ (their projects) |
| Work plan | ✓ | | ✓ | ✓ |
| Progress updates | ✓ | | ✓ | ✓ |
| Approvals | ✓ | | ✓ | ✓ |
| Change requests | ✓ | | | ✓ |
| Risks | ✓ | | ✓ | ✓ |
| Timeline | ✓ | ✓ | ✓ | ✓ |
| Data (import/export) | ✓ | | | |
| Users | ✓ | | | |

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

## Who can get in
Only people added to Tamkeen get any data. That means an owner, anyone on the Users list (PMO, sector lead or executive), anyone in the directory, and project managers in the PM roster. Anyone else can sign in but only sees "This email isn't registered in Tamkeen". The server refuses them all data, files and documents.

**Test link (Codespace).** Port 5173 is set to **Public**, so the link opens on any device without a GitHub account. The test sign-in asks only for an email. Anyone who has the link and knows a registered email can therefore sign in as that person. Keep the test link for test data and trusted testers. Production uses Microsoft sign-in (Entra ID).
