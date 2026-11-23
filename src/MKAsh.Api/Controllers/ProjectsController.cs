using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MKAsh.Api.Data;
using MKAsh.Api.Services;
using MKAsh.Core.Domain;
using MKAsh.Core.Rules;

namespace MKAsh.Api.Controllers;

public sealed record ActivityChange(int Progress, ActivityStatus Status, string? Note, DateOnly? ActualStart, DateOnly? ActualEnd);

[ApiController, Authorize, Route("api/projects")]
public class ProjectsController(PmsDbContext db, CurrentUser me, PortfolioService portfolio) : ControllerBase
{
    /// <summary>Projects the caller can see. Executives always get approved figures and no overdue counts.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? lead, CancellationToken ct)
    {
        var u = await me.RequirePageAsync("projects", ct);
        return Ok(await portfolio.ProjectsAsync(u, approvedOnly: u.Role == Role.Executive, leadFilter: lead, ct: ct));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var u = await me.RequirePageAsync("projects", ct);
        await portfolio.LoadVisibleAsync(u, id, ct);
        var p = await db.Projects.AsNoTracking()
            .Include(x => x.Initiative).ThenInclude(i => i!.Portfolio)
            .Include(x => x.WorkPackages).ThenInclude(w => w.Activities)
            .AsSplitQuery().FirstAsync(x => x.Id == id, ct);
        var today = portfolio.Today;
        var approvedOnly = u.Role == Role.Executive;
        var view = PortfolioService.View(p, today, await portfolio.ModeAsync(ct), await portfolio.TemplatesAsync(ct), approvedOnly, AccessRules.ShowsOverdue(u));
        var showOverdue = AccessRules.ShowsOverdue(u);

        var packages = p.WorkPackages.OrderBy(w => w.Order).Select(w => new
        {
            w.Id, w.Name, Phase = w.PhaseOverride?.ToString() ?? PhaseKeywords.AutoPhase(w.Name).ToString(), PhaseOverridden = w.PhaseOverride.HasValue,
            Activities = w.Activities.OrderBy(a => a.Id).Select(a =>
            {
                var row = new ActivityRow(w.Name, a.Title, a.PlannedStart, a.PlannedEnd, approvedOnly ? a.ApprovedProgress : a.Progress, approvedOnly ? a.ApprovedStatus : a.Status);
                var state = ProgressRules.State(row, today);
                if (!showOverdue && state == ActivityState.Overdue) state = row.Progress > 0 ? ActivityState.InProgress : ActivityState.NotStarted;
                return new
                {
                    a.Id, a.Title, a.PlannedStart, a.PlannedEnd, a.ActualStart, a.ActualEnd,
                    Progress = row.Progress, Status = PortfolioService.Camel(row.Status), State = PortfolioService.Camel(state),
                    Planned = ProgressRules.PlannedAt(row, today), a.ManagerNote, a.PmoNote,
                    PendingApproval = !approvedOnly && (a.Progress != a.ApprovedProgress || a.Status != a.ApprovedStatus),
                };
            }),
        });
        var risks = await db.Risks.AsNoTracking().Where(r => r.ProjectId == id).OrderByDescending(r => r.Probability * r.Impact).ToListAsync(ct);
        var milestones = await db.Milestones.AsNoTracking().Where(m => m.ProjectId == id).OrderBy(m => m.Due).ToListAsync(ct);
        return Ok(new
        {
            Project = view, p.Description, p.Objective, p.Priority, p.PqiNote,
            Initiative = p.Initiative is null ? null : new { p.Initiative.Code, p.Initiative.NameAr, p.Initiative.NameEn },
            CanEdit = AccessRules.CanEditProject(u, id),
            WorkPackages = packages, Risks = risks.Select(r => new { r.Id, r.TitleAr, r.TitleEn, r.Category, r.Probability, r.Impact, r.Score, r.Owner, r.Status, r.Due, r.Mitigation }),
            Milestones = milestones,
        });
    }

    /// <summary>
    /// A manager updates an activity. The live value changes at once (the manager sees it);
    /// leadership pages keep showing the approved value until the lead or PMO approves.
    /// PMO changes are approved immediately.
    /// </summary>
    [HttpPut("{id:int}/activities/{activityId:int}")]
    public async Task<IActionResult> UpdateActivity(int id, int activityId, ActivityChange body, CancellationToken ct)
    {
        var u = await me.RequireAsync(ct);
        if (!AccessRules.CanEditProject(u, id)) throw new ForbiddenException("You cannot edit this project.");
        var project = await db.Projects.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Project not found.");
        if (project.StatusOverride == ProjectStatusOverride.Closed) throw new ConflictException("This project is closed.");
        var a = await db.Activities.Include(x => x.WorkPackage).FirstOrDefaultAsync(x => x.Id == activityId && x.WorkPackage!.ProjectId == id, ct)
                ?? throw new NotFoundException("Activity not found.");
        if (body.Progress is < 0 or > 100) return BadRequest(new { error = "Progress must be 0–100." });

        var change = new
        {
            ActivityId = activityId,
            Before = new { a.Progress, Status = a.Status.ToString() },
            After = new { body.Progress, Status = body.Status.ToString() },
        };
        a.Progress = body.Progress;
        a.Status = body.Status;
        a.ActualStart = body.ActualStart ?? a.ActualStart;
        a.ActualEnd = body.ActualEnd ?? a.ActualEnd;
        if (body.Note is not null) a.ManagerNote = body.Note;

        var status = AccessRules.InitialApproval(u);
        if (status == ApprovalStatus.Approved) { a.ApprovedProgress = a.Progress; a.ApprovedStatus = a.Status; }
        var freq = await CycleAsync(ct);
        db.Updates.Add(new ProgressUpdate
        {
            ProjectId = id, Kind = UpdateKind.Progress, Period = ReportingPeriods.Of(freq, portfolio.Today).Key,
            Action = "activity", Title = a.Title, ChangesJson = JsonSerializer.Serialize(new[] { change }), Note = body.Note,
            SubmittedById = u.PersonId, SubmittedAt = DateTime.UtcNow, Status = status, AutoApproved = status == ApprovalStatus.Approved,
        });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>The PMO overrides the phase a work package counts under in the weighted view.</summary>
    [HttpPut("{id:int}/work-packages/{wpId:int}/phase")]
    public async Task<IActionResult> SetPhase(int id, int wpId, [FromBody] WeightPhase? phase, CancellationToken ct)
    {
        var u = await me.RequireAsync(ct);
        if (u.Role != Role.Pmo) throw new ForbiddenException("Only the PMO can change phases.");
        var wp = await db.WorkPackages.FirstOrDefaultAsync(w => w.Id == wpId && w.ProjectId == id, ct) ?? throw new NotFoundException("Work package not found.");
        wp.PhaseOverride = phase;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<ReportingFrequency> CycleAsync(CancellationToken ct) =>
        Enum.TryParse<ReportingFrequency>((await db.Settings.FindAsync(new object[] { "updateCycle" }, ct))?.Value, out var f) ? f : ReportingFrequency.Biweekly;
}
