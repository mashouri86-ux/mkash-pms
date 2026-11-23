using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MKAsh.Api.Data;
using MKAsh.Api.Services;
using MKAsh.Core.Domain;
using MKAsh.Core.Rules;

namespace MKAsh.Api.Controllers;

/// <summary>Body for creating or saving a change request. Form/Apply/Npc follow the template keys.</summary>
public sealed record ChangeRequestBody(int ProjectId, JsonElement Form, JsonElement? Apply, JsonElement? Npc);
public sealed record NpcDecisionBody(string Decision, JsonElement? Npc);

/// <summary>
/// NDS3 change requests. Route: project manager (or PMO) drafts → sector lead → PMO → NPC.
/// The PMO records the NPC decision; "approved" applies the change to the project automatically.
/// </summary>
[ApiController, Authorize, Route("api/change-requests")]
public class ChangeRequestsController(PmsDbContext db, CurrentUser me, PortfolioService portfolio, ChangeRequestService crs) : ControllerBase
{
    [HttpGet("lists")]
    public async Task<IActionResult> Lists(CancellationToken ct)
    {
        await me.RequirePageAsync("changes", ct);
        return Content(crs.Lists.ToJsonString(), "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var u = await me.RequireAsync(ct);
        var all = await db.ChangeRequests.AsNoTracking().Include(c => c.Project).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        var leads = await LeadsWithAccountsAsync(ct);
        return Ok(all.Where(c => Visible(u, c, leads)).Select(c => new
        {
            c.Id, c.Code, c.ProjectId, ProjectCode = c.Project!.Code, ProjectNameAr = c.Project.NameAr, Status = PortfolioService.Camel(c.Status),
            Form = JsonDocument.Parse(c.FormJson).RootElement, c.CreatedAt, c.SubmittedAt, c.AppliedAt,
            CanDecide = WorkflowRules.CanDecide(u, c.Status, c.Project.SectorLead, leads.Contains(c.Project.SectorLead ?? "")),
        }));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var u = await me.RequireAsync(ct);
        var c = await LoadAsync(u, id, ct);
        var leads = await LeadsWithAccountsAsync(ct);
        return Ok(Dto(u, c, leads));
    }

    [HttpPost]
    public async Task<IActionResult> Create(ChangeRequestBody body, CancellationToken ct)
    {
        var u = await me.RequirePageAsync("changes", ct);
        if (!AccessRules.CanEditProject(u, body.ProjectId)) throw new ForbiddenException("You cannot raise a change request for this project.");
        var c = new ChangeRequest
        {
            ProjectId = body.ProjectId, Status = ChangeRequestStatus.Draft, FormJson = body.Form.GetRawText(),
            ApplyJson = Raw(body.Apply), CreatedById = u.PersonId, CreatedAt = DateTime.UtcNow,
        };
        c.History.Add(Event(u, "created"));
        db.ChangeRequests.Add(c);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = c.Id }, new { c.Id });
    }

    /// <summary>Owners save sections 1–3 while Draft/Returned; the PMO saves sections 4–6 at any stage before the decision.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Save(int id, ChangeRequestBody body, CancellationToken ct)
    {
        var u = await me.RequireAsync(ct);
        var c = await LoadAsync(u, id, ct);
        var owner = AccessRules.CanEditProject(u, c.ProjectId) && WorkflowRules.CanEdit(c.Status);
        if (owner)
        {
            c.FormJson = body.Form.GetRawText();
            c.ApplyJson = Raw(body.Apply);
        }
        if (u.Role == Role.Pmo && Raw(body.Npc) is { } npc && c.Status is not (ChangeRequestStatus.Approved or ChangeRequestStatus.Rejected))
            c.NpcJson = npc;
        else if (!owner) throw new ForbiddenException("This change request cannot be edited now.");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:int}/submit")]
    public async Task<IActionResult> Submit(int id, CancellationToken ct)
    {
        var u = await me.RequireAsync(ct);
        var c = await LoadAsync(u, id, ct);
        if (!AccessRules.CanEditProject(u, c.ProjectId) || !WorkflowRules.CanEdit(c.Status)) throw new ForbiddenException("This request cannot be submitted.");
        var missing = ChangeRequestService.Missing(c);
        if (missing.Count > 0) return BadRequest(new { error = "missingFields", fields = missing });
        c.Code = await crs.CodeAsync(c, ct) ?? throw new ConflictException("The request code could not be built from the form.");
        var resubmit = c.Status == ChangeRequestStatus.Returned;
        c.Status = u.Role == Role.Pmo ? ChangeRequestStatus.WithNpc : WorkflowRules.OnSubmit(await portfolio.HasLeadAccountAsync(c.Project!.SectorLead, ct));
        c.SubmittedAt = DateTime.UtcNow;
        c.ReturnNote = null;
        c.History.Add(Event(u, resubmit ? "resubmitted" : "submitted"));
        await db.SaveChangesAsync(ct);
        return Ok(new { c.Code, Status = PortfolioService.Camel(c.Status) });
    }

    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, Decision d, CancellationToken ct)
    {
        var (u, c) = await DecidableAsync(id, ct);
        c.Status = WorkflowRules.OnApprove(c.Status);
        c.History.Add(Event(u, c.Status == ChangeRequestStatus.WithNpc ? "pmo-approved" : "lead-approved", d.Note));
        await db.SaveChangesAsync(ct);
        return Ok(new { Status = PortfolioService.Camel(c.Status) });
    }

    [HttpPost("{id:int}/return")]
    public async Task<IActionResult> Return(int id, Decision d, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.Note)) return BadRequest(new { error = "Say what needs changing." });
        var (u, c) = await DecidableAsync(id, ct);
        c.Status = ChangeRequestStatus.Returned;
        c.ReturnNote = d.Note;
        c.History.Add(Event(u, "returned", d.Note));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>PMO records the NPC decision (Yes / No / more information). Yes applies the change.</summary>
    [HttpPost("{id:int}/npc")]
    public async Task<IActionResult> Npc(int id, NpcDecisionBody body, CancellationToken ct)
    {
        var u = await me.RequireAsync(ct);
        if (u.Role != Role.Pmo) throw new ForbiddenException("Only the PMO records NPC decisions.");
        var c = await LoadAsync(u, id, ct);
        if (c.Status != ChangeRequestStatus.WithNpc) throw new ConflictException("This request is not with the NPC.");
        if (Raw(body.Npc) is { } npc) c.NpcJson = npc;
        c.Status = WorkflowRules.OnNpcDecision(body.Decision);
        List<string> applied = new();
        if (c.Status == ChangeRequestStatus.Approved) applied = await crs.ApplyAsync(c, ct);
        if (c.Status == ChangeRequestStatus.Returned) c.ReturnNote = "NPC requested more information";
        c.History.Add(Event(u, $"npc-{body.Decision}", applied.Count > 0 ? string.Join("; ", applied) : null));
        await db.SaveChangesAsync(ct);
        return Ok(new { Status = PortfolioService.Camel(c.Status), Applied = applied });
    }

    /// <summary>The original NDS3 Excel template (.xlsm) filled in, as the NPC requires.</summary>
    [HttpGet("{id:int}/excel")]
    public async Task<IActionResult> Excel(int id, CancellationToken ct)
    {
        var u = await me.RequireAsync(ct);
        var c = await LoadAsync(u, id, ct);
        var bytes = await crs.WorkbookAsync(c, ct);
        return File(bytes, "application/vnd.ms-excel.sheet.macroEnabled.12", $"{c.Code ?? "change-request-draft"}.xlsm");
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var u = await me.RequireAsync(ct);
        var c = await LoadAsync(u, id, ct);
        if (c.Status != ChangeRequestStatus.Draft || !AccessRules.CanEditProject(u, c.ProjectId)) throw new ForbiddenException("Only drafts can be deleted.");
        db.ChangeRequests.Remove(c);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- helpers ----

    private static string? Raw(JsonElement? e) =>
        e is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } v ? v.GetRawText() : null;

    private async Task<HashSet<string>> LeadsWithAccountsAsync(CancellationToken ct) =>
        (await db.People.Where(p => p.Role == Role.Lead && p.Active && p.LeadName != null).Select(p => p.LeadName!).ToListAsync(ct)).ToHashSet();

    private static bool Visible(UserContext u, ChangeRequest c, HashSet<string> leads) => u.Role switch
    {
        Role.Pmo => true,
        Role.ProjectManager => u.ManagedProjectIds.Contains(c.ProjectId),
        Role.Lead => AccessRules.LeadMatches(c.Project!.SectorLead, u.LeadName),
        _ => false,
    };

    private async Task<ChangeRequest> LoadAsync(UserContext u, int id, CancellationToken ct)
    {
        var c = await db.ChangeRequests.Include(x => x.Project).Include(x => x.History).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("Change request not found.");
        if (!Visible(u, c, await LeadsWithAccountsAsync(ct))) throw new ForbiddenException("You cannot see this change request.");
        return c;
    }

    private async Task<(UserContext, ChangeRequest)> DecidableAsync(int id, CancellationToken ct)
    {
        var u = await me.RequireAsync(ct);
        var c = await LoadAsync(u, id, ct);
        if (!WorkflowRules.CanDecide(u, c.Status, c.Project!.SectorLead, await portfolio.HasLeadAccountAsync(c.Project.SectorLead, ct)))
            throw new ForbiddenException("You cannot decide this request at this stage.");
        return (u, c);
    }

    private static WorkflowEvent Event(UserContext u, string action, string? note = null) =>
        new() { Action = action, Note = note, ById = u.PersonId, At = DateTime.UtcNow };

    private object Dto(UserContext u, ChangeRequest c, HashSet<string> leads) => new
    {
        c.Id, c.Code, c.ProjectId, ProjectCode = c.Project!.Code, ProjectNameAr = c.Project.NameAr, Status = PortfolioService.Camel(c.Status),
        Form = JsonDocument.Parse(c.FormJson).RootElement,
        Apply = c.ApplyJson is null ? (JsonElement?)null : JsonDocument.Parse(c.ApplyJson).RootElement,
        Npc = c.NpcJson is null ? (JsonElement?)null : JsonDocument.Parse(c.NpcJson).RootElement,
        c.ReturnNote, c.CharterUrl, c.CreatedAt, c.SubmittedAt, c.AppliedAt,
        History = c.History.OrderBy(h => h.At).Select(h => new { h.Action, h.Note, h.ById, h.At }),
        CanEdit = AccessRules.CanEditProject(u, c.ProjectId) && WorkflowRules.CanEdit(c.Status),
        CanDecide = WorkflowRules.CanDecide(u, c.Status, c.Project.SectorLead, leads.Contains(c.Project.SectorLead ?? "")),
        CanRecordNpc = u.Role == Role.Pmo && c.Status == ChangeRequestStatus.WithNpc,
    };
}
