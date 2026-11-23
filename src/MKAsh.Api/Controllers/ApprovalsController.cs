using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MKAsh.Api.Data;
using MKAsh.Api.Services;
using MKAsh.Core.Domain;
using MKAsh.Core.Rules;

namespace MKAsh.Api.Controllers;

public sealed record Decision(string? Note);

/// <summary>Progress updates waiting for the sector lead or the PMO (the bell in the web app).</summary>
[ApiController, Authorize, Route("api/approvals")]
public class ApprovalsController(PmsDbContext db, CurrentUser me, PortfolioService portfolio) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public sealed record ChangeLine(int ActivityId);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string status = "pending", CancellationToken ct = default)
    {
        var u = await me.RequirePageAsync("approvals", ct);
        var wanted = Enum.TryParse<ApprovalStatus>(status, true, out var s) ? s : ApprovalStatus.Pending;
        var rows = await db.Updates.AsNoTracking().Include(x => x.Project)
            .Where(x => x.Status == wanted).OrderByDescending(x => x.SubmittedAt).Take(500).ToListAsync(ct);
        var leadsWithAccounts = (await db.People.Where(p => p.Role == Role.Lead && p.Active && p.LeadName != null).Select(p => p.LeadName!).ToListAsync(ct)).ToHashSet();
        var names = await db.People.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.NameAr, ct);

        var visible = rows.Where(x => u.Role switch
        {
            Role.ProjectManager => u.ManagedProjectIds.Contains(x.ProjectId),
            _ => AccessRules.CanApprove(u, x.Project!.SectorLead, leadsWithAccounts.Contains(x.Project!.SectorLead ?? "")),
        });
        return Ok(visible.Select(x => new
        {
            x.Id, x.ProjectId, x.Project!.Code, ProjectNameAr = x.Project.NameAr, Kind = PortfolioService.Camel(x.Kind), x.Period, x.Action, x.Title,
            x.ChangesJson, x.Note, SubmittedBy = names.GetValueOrDefault(x.SubmittedById), x.SubmittedAt, Status = PortfolioService.Camel(x.Status), x.ReviewNote,
            CanDecide = x.Status == ApprovalStatus.Pending && AccessRules.CanApprove(u, x.Project.SectorLead, leadsWithAccounts.Contains(x.Project.SectorLead ?? "")),
        }));
    }

    [HttpPost("{id:int}/approve")]
    public Task<IActionResult> Approve(int id, Decision d, CancellationToken ct) => Decide(id, true, d, ct);

    [HttpPost("{id:int}/return")]
    public Task<IActionResult> Return(int id, Decision d, CancellationToken ct) => Decide(id, false, d, ct);

    private async Task<IActionResult> Decide(int id, bool approve, Decision d, CancellationToken ct)
    {
        var u = await me.RequireAsync(ct);
        var up = await db.Updates.Include(x => x.Project).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Update not found.");
        if (up.Status != ApprovalStatus.Pending) throw new ConflictException("This update has already been decided.");
        if (!AccessRules.CanApprove(u, up.Project!.SectorLead, await portfolio.HasLeadAccountAsync(up.Project.SectorLead, ct)))
            throw new ForbiddenException("You cannot decide this update.");
        if (!approve && string.IsNullOrWhiteSpace(d.Note)) return BadRequest(new { error = "Say what needs fixing when returning an update." });

        var ids = string.IsNullOrEmpty(up.ChangesJson) ? new List<int>()
            : (JsonSerializer.Deserialize<List<ChangeLine>>(up.ChangesJson, Json) ?? new()).Select(c => c.ActivityId).ToList();
        var acts = await db.Activities.Where(a => ids.Contains(a.Id)).ToListAsync(ct);
        foreach (var a in acts)
        {
            if (approve) { a.ApprovedProgress = a.Progress; a.ApprovedStatus = a.Status; }
            else { a.Progress = a.ApprovedProgress; a.Status = a.ApprovedStatus; }
        }
        up.Status = approve ? ApprovalStatus.Approved : ApprovalStatus.Returned;
        up.ReviewedById = u.PersonId;
        up.ReviewedAt = DateTime.UtcNow;
        up.ReviewNote = d.Note;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
