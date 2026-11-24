# Business rules

These rules are ported line by line from the prototype. `GoldenTests` proves the .NET version gives the same figures.

## Activities
- **Done** when progress is 100%, or when the activity is marked done with progress above 0%. Marked done at 0% does **not** count as done.
- **Overdue** when the planned end has passed and the activity is not done. This is calculated, never stored.
- **Planned % at a date:** 0 before the planned start, 100 after the planned end, and a straight line in between.

## Project progress (two views; the PMO chooses in Administration)
- **Average % complete:** the mean of activity progress. Planned is the mean of activity planned %.
- **Weighted by phase:**
  1. Remove exact duplicate activities (same title, start and end).
  2. Work-package progress is the duration-weighted mean of its activities.
  3. Each work package belongs to a phase (Initiation, Procurement, Execution, Launch, Closure). The phase comes from keywords in its name, or from a PMO override.
  4. Phase weights come from the project-type template, rescaled over the phases that are present. Within a phase, packages share the weight by planned span.
  5. The project type (Study, Policy, Build, Program) is detected from the name or set by the PMO.

| Type | Initiation | Procurement | Execution | Launch | Closure |
|---|--:|--:|--:|--:|--:|
| Study | 10 | 15 | 50 | 15 | 10 |
| Policy | 10 | 10 | 35 | 35 | 10 |
| Build | 10 | 20 | 45 | 15 | 10 |
| Program | 10 | 15 | 50 | 15 | 10 |

- A project with no activities uses its manual progress, and planned % from its start and end dates.
- Completed and Closed projects count planned as 100%.

## Status
Variance = progress − planned.
- **Completed** when progress ≥ 99.5%.
- **On track** when variance ≥ −5.
- **At risk** when variance ≥ −15.
- **Delayed** otherwise.
- A stored status (On hold, Completed, Closed, Cancelled) always wins.

## KPIs
- Delivery = impact-weighted progress of linked projects (high 3, medium 2, low 1; Ministry KPIs use 1 for all).
- Closed and completed projects count as 100%.
- Status uses the same bands. It is Completed when every linked project is done.
- Achievement = (current − baseline) ÷ (target − baseline), shown when the values exist.

## Update cycle and reports
- Managers update **bi-weekly** by default. Cycles run Sunday to the second Saturday, counted from Sunday 5 January 2025. The PMO can switch to monthly or quarterly.
- Reports can be bi-weekly, monthly, quarterly or yearly, for the current period or any earlier one.

## Attachments (everywhere in Tamkeen)
- Accepted types: **PDF, Word (.doc/.docx), Excel (.xls/.xlsx/.xlsm) and images (PNG/JPG)**, up to 20 MB each. The server refuses anything else.
- At most **3 files** per place:
  - per activity (proof of completion);
  - per change-request attachment type (updated charter, filled template);
  - per closure request.
- Every file name in Tamkeen opens the file directly, including on items in Approvals. When proof of completion is **returned**, the project manager can attach more files right from the returned item, up to the limit of 3.

## Change requests
- **Requester** (field 1.3) defaults to the project's **project manager**. It can be switched to any member of the PMO team from the Users list.
- Two attachment slots, each up to 3 files:
  - **Updated project charter**;
  - **Filled change request template (Excel)**, which the NPC asks for.
- When a request is returned, the approvals item shows who returned it and links to its attachments.

## Who is recorded
Approvals, returns and submissions record the person who did them. When the owner previews the system as someone else (⇄ Switch user), actions are recorded under that person, the same as if they had signed in.
