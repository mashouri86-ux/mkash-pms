using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MKAsh.Api.Data;
using MKAsh.Api.Services;
using MKAsh.Core.Domain;
using MKAsh.Core.Rules;

namespace MKAsh.Api.Controllers;

public sealed record PersonBody(string NameAr, string NameEn, string Email, Role Role, string? LeadName, bool Active, int[]? ManagedProjectIds);
public sealed record SettingsBody(ReportingFrequency? UpdateCycle, ProgressMode? ProgressMode);

/// <summary>PMO administration: people and roles, the update cycle, the progress view and phase weights.</summary>
[ApiController, Authorize, Route("api/admin")]
public class AdminController(PmsDbContext db, CurrentUser me, PortfolioService portfolio) : ControllerBase
{
    private async Task<UserContext> Pmo(CancellationToken ct)
    {
        var u = await me.RequireAsync(ct);
        if (u.Role != Role.Pmo) throw new ForbiddenException("PMO only.");
        return u;
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        await me.RequireAsync(ct);
        var cycle = Enum.TryParse<ReportingFrequency>((await db.Settings.FindAsync(new object[] { "updateCycle" }, ct))?.Value, out var f) ? f : ReportingFrequency.Biweekly;
        var current = ReportingPeriods.Of(cycle, portfolio.Today);
        return Ok(new
        {
            UpdateCycle = PortfolioService.Camel(cycle),
            CurrentCycle = new { current.Start, current.End, current.Key },
            ProgressMode = PortfolioService.Camel(await portfolio.ModeAsync(ct)),
            Templates = (await portfolio.TemplatesAsync(ct)).ToDictionary(kv => PortfolioService.Camel(kv.Key), kv => kv.Value.ToDictionary(x => PortfolioService.Camel(x.Key), x => x.Value)),
        });
    }

    [HttpPut("settings")]
    public async Task<IActionResult> PutSettings(SettingsBody body, CancellationToken ct)
    {
        await Pmo(ct);
        if (body.UpdateCycle is { } c) await Set("updateCycle", c.ToString(), ct);
        if (body.ProgressMode is { } m) await Set("progressMode", m == ProgressMode.Weighted ? "weighted" : "pct", ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("weights/{type}")]
    public async Task<IActionResult> PutWeights(ProjectType type, Dictionary<WeightPhase, int> row, CancellationToken ct)
    {
        await Pmo(ct);
        if (!WeightTemplates.IsValid(row) || row.Count != 5) return BadRequest(new { error = "Give all five phases; weights must total 100." });
        var existing = await db.WeightTemplates.Where(w => w.Type == type).ToListAsync(ct);
        db.WeightTemplates.RemoveRange(existing);
        foreach (var (phase, w) in row) db.WeightTemplates.Add(new WeightTemplate { Type = type, Phase = phase, Weight = w });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("people")]
    public async Task<IActionResult> People(CancellationToken ct)
    {
        await Pmo(ct);
        var managed = await db.Projects.Where(p => p.ManagerId != null).GroupBy(p => p.ManagerId!.Value).ToDictionaryAsync(g => g.Key, g => g.Select(p => p.Id).ToList(), ct);
        var people = await db.People.AsNoTracking().OrderBy(p => p.Role).ThenBy(p => p.NameAr).ToListAsync(ct);
        return Ok(people.Select(p => new { p.Id, p.NameAr, p.NameEn, p.Email, Role = PortfolioService.Camel(p.Role), p.LeadName, p.Active, ManagedProjectIds = managed.GetValueOrDefault(p.Id) ?? new List<int>() }));
    }

    [HttpPost("people")]
    public async Task<IActionResult> AddPerson(PersonBody body, CancellationToken ct)
    {
        await Pmo(ct);
        var email = body.Email.Trim().ToLowerInvariant();
        if (await db.People.AnyAsync(p => p.Email == email, ct)) throw new ConflictException("That email is already registered.");
        var p = new Person { NameAr = body.NameAr, NameEn = body.NameEn, Email = email, Role = body.Role, LeadName = body.LeadName, Active = body.Active };
        db.People.Add(p);
        await db.SaveChangesAsync(ct);
        await AssignProjects(p, body.ManagedProjectIds, ct);
        return Ok(new { p.Id });
    }

    [HttpPut("people/{id:int}")]
    public async Task<IActionResult> UpdatePerson(int id, PersonBody body, CancellationToken ct)
    {
        await Pmo(ct);
        var p = await db.People.FindAsync(new object[] { id }, ct) ?? throw new NotFoundException("Person not found.");
        (p.NameAr, p.NameEn, p.Email, p.Role, p.LeadName, p.Active) = (body.NameAr, body.NameEn, body.Email.Trim().ToLowerInvariant(), body.Role, body.LeadName, body.Active);
        await AssignProjects(p, body.ManagedProjectIds, ct);
        return NoContent();
    }

    private async Task AssignProjects(Person p, int[]? ids, CancellationToken ct)
    {
        if (ids is not null && p.Role == Role.ProjectManager)
        {
            foreach (var pr in await db.Projects.Where(x => x.ManagerId == p.Id && !ids.Contains(x.Id)).ToListAsync(ct)) pr.ManagerId = null;
            foreach (var pr in await db.Projects.Where(x => ids.Contains(x.Id)).ToListAsync(ct)) { pr.ManagerId = p.Id; pr.ManagerName = p.NameAr; }
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task Set(string key, string value, CancellationToken ct)
    {
        var s = await db.Settings.FindAsync(new object[] { key }, ct);
        if (s is null) db.Settings.Add(new Setting { Key = key, Value = value });
        else s.Value = value;
    }
}
