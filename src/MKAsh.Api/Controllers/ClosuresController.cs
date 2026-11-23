using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MKAsh.Api.Data;
using MKAsh.Api.Services;
using MKAsh.Core.Domain;
using MKAsh.Core.Rules;

namespace MKAsh.Api.Controllers;

public sealed record ClosureRequestBody(string Summary, string? Lessons, Dictionary<int, string?>? DeliverableNotes);
public sealed record AcceptanceBody(DeliverableAcceptance Acceptance, string? Note);

/// <summary>
/// Project closure: the project manager requests it, the sector lead (or the PMO when there is no lead)
/// accepts every deliverable and approves; the project then becomes Closed.
/// </summary>
[ApiController, Authorize, Route("api/projects/{projectId:int}/closure")]
public class ClosuresController(PmsDbContext db, CurrentUser me, PortfolioService portfolio) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(int projectId, CancellationToken ct)
    {
        var u = await me.RequireAsync(ct);
        var p = await portfolio.LoadVisibleAsync(u, projectId, ct);
        var c = await Latest(projectId, ct);
        if (c is null) return NoContent();
        var canDecide = c.Status == ClosureStatus.Pending && AccessRules.CanApprove(u, p.SectorLead, await portfolio.HasLeadAccountAsync(p.SectorLead, ct));
        return Ok(new
        {
            c.Id, Status = PortfolioService.Camel(c.Status), c.Summary, c.Lessons, c.ReturnNote, c.RequestedAt, c.DecidedAt,
            Deliverables = c.Deliverables.OrderBy(d => d.Id).Select(d => new { d.Id, d.WorkPackageId, d.Name, d.Progress, d.ManagerNote, Acceptance = PortfolioService.Camel(d.Acceptance), d.AcceptanceNote }),
            CanDecide = canDecide, CanApprove = canDecide && WorkflowRules.CanApproveClosure(c.Deliverables),
        });
    }

    /// <summary>One deliverable per work package, with its current progress.</summary>
    [HttpPost]
    public async Task<IActionResult> RequestClosure(int projectId, ClosureRequestBody body, CancellationToken ct)
    {
        var u = await me.RequireAsync(ct);
        if (!AccessRules.CanEditProject(u, projectId)) throw new ForbiddenException("Only the project manager or the PMO can request closure.");
        var p = await db.Projects.Include(x => x.WorkPackages).ThenInclude(w => w.Activities).FirstOrDefaultAsync(x => x.Id == projectId, ct)
                ?? throw new NotFoundException("Project not found.");
        if (p.StatusOverride == ProjectStatusOverride.Closed) throw new ConflictException("The project is already closed.");
        if ((await Latest(projectId, ct))?.Status == ClosureStatus.Pending) throw new ConflictException("A closure request is already waiting.");
        if (string.IsNullOrWhiteSpace(body.Summary)) return BadRequest(new { error = "Add a closing summary." });

        var c = new Closure { ProjectId = projectId, Status = ClosureStatus.Pending, Summary = body.Summary, Lessons = body.Lessons, RequestedById = u.PersonId, RequestedAt = DateTime.UtcNow };
        foreach (var w in p.WorkPackages.OrderBy(w => w.Order))
        {
            var acts = w.Activities;
            var prog = acts.Count == 0 ? 0 : (int)Math.Round(acts.Average(a => (double)a.Progress));
            c.Deliverables.Add(new ClosureDeliverable
            {
                WorkPackageId = w.Id, Name = w.Name, Progress = prog, Acceptance = DeliverableAcceptance.Awaiting,
                ManagerNote = body.DeliverableNotes?.GetValueOrDefault(w.Id),
            });
        }
        c.History.Add(new WorkflowEvent { Action = "requested", ById = u.PersonId, At = DateTime.UtcNow });
        db.Closures.Add(c);
        await db.SaveChangesAsync(ct);
        return Ok(new { c.Id });
    }

    [HttpPut("deliverables/{deliverableId:int}")]
    public async Task<IActionResult> SetAcceptance(int projectId, int deliverableId, AcceptanceBody body, CancellationToken ct)
    {
        var (u, c) = await Decidable(projectId, ct);
        var d = c.Deliverables.FirstOrDefault(x => x.Id == deliverableId) ?? throw new NotFoundException("Deliverable not found.");
        if (body.Acceptance == DeliverableAcceptance.Rejected && string.IsNullOrWhiteSpace(body.Note)) return BadRequest(new { error = "Say why the deliverable is not accepted." });
        d.Acceptance = body.Acceptance;
        d.AcceptanceNote = body.Note;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("approve")]
    public async Task<IActionResult> Approve(int projectId, Decision d, CancellationToken ct)
    {
        var (u, c) = await Decidable(projectId, ct);
        if (!WorkflowRules.CanApproveClosure(c.Deliverables)) throw new ConflictException("Accept every deliverable before approving closure.");
        var p = await db.Projects.FirstAsync(x => x.Id == projectId, ct);
        p.StatusOverride = ProjectStatusOverride.Closed;
        p.Phase = ProjectPhase.Closure;
        p.ClosedAt = DateTime.UtcNow;
        c.Status = ClosureStatus.Approved;
        c.DecidedById = u.PersonId;
        c.DecidedAt = DateTime.UtcNow;
        c.History.Add(new WorkflowEvent { Action = "approved", Note = d.Note, ById = u.PersonId, At = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("return")]
    public async Task<IActionResult> Return(int projectId, Decision d, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.Note)) return BadRequest(new { error = "Say what is missing." });
        var (u, c) = await Decidable(projectId, ct);
        c.Status = ClosureStatus.Returned;
        c.ReturnNote = d.Note;
        c.DecidedById = u.PersonId;
        c.DecidedAt = DateTime.UtcNow;
        c.History.Add(new WorkflowEvent { Action = "returned", Note = d.Note, ById = u.PersonId, At = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<Closure?> Latest(int projectId, CancellationToken ct) =>
        db.Closures.Include(c => c.Deliverables).Include(c => c.History).Where(c => c.ProjectId == projectId).OrderByDescending(c => c.RequestedAt).FirstOrDefaultAsync(ct);

    private async Task<(UserContext, Closure)> Decidable(int projectId, CancellationToken ct)
    {
        var u = await me.RequireAsync(ct);
        var p = await portfolio.LoadVisibleAsync(u, projectId, ct);
        var c = await Latest(projectId, ct);
        if (c is null || c.Status != ClosureStatus.Pending) throw new ConflictException("No closure request is waiting.");
        if (!AccessRules.CanApprove(u, p.SectorLead, await portfolio.HasLeadAccountAsync(p.SectorLead, ct))) throw new ForbiddenException("You cannot decide this closure.");
        return (u, c);
    }
}
