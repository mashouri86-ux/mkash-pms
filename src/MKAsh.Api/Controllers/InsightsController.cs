using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MKAsh.Api.Data;
using MKAsh.Api.Services;
using MKAsh.Core.Domain;
using MKAsh.Core.Rules;

namespace MKAsh.Api.Controllers;

/// <summary>KPIs, the leadership summary and period reports.</summary>
[ApiController, Authorize, Route("api")]
public class InsightsController(PmsDbContext db, CurrentUser me, PortfolioService portfolio) : ControllerBase
{
    /// <summary>KPIs with baseline, target and delivery progress from linked projects. PMO and executives only.</summary>
    [HttpGet("kpis")]
    public async Task<IActionResult> Kpis([FromQuery] KpiCategory? category, CancellationToken ct)
    {
        var u = await me.RequirePageAsync("kpis", ct);
        var projects = (await portfolio.ProjectsAsync(u, approvedOnly: u.Role == Role.Executive, ct: ct)).ToDictionary(p => p.Id);
        var kpis = await db.Kpis.AsNoTracking().Include(k => k.Links)
            .Where(k => category == null || k.Category == category).OrderBy(k => k.Category).ThenBy(k => k.Id).ToListAsync(ct);

        return Ok(kpis.Select(k =>
        {
            var linked = k.Links.Where(l => projects.ContainsKey(l.ProjectId)).Select(l => (l, p: projects[l.ProjectId])).ToList();
            var d = KpiRules.Delivery(linked.Select(x => new KpiProjectInput(x.p.Progress, x.p.Planned, Enum.Parse<ProjectStatus>(x.p.Status, true),
                k.Category == KpiCategory.Ministry ? 1 : x.l.Impact)).ToList());
            return new
            {
                k.Id, Category = PortfolioService.Camel(k.Category), k.NameAr, k.NameEn, k.Outcome, k.Objective, k.Unit,
                k.Baseline, k.BaselineYear, k.Target, k.TargetYear, k.Current, k.CurrentYear, k.DataOwner,
                Achievement = KpiRules.Achievement(k.Baseline, k.Current, k.Target),
                Delivery = new { d.LinkedProjects, Progress = Math.Round(d.Progress, 2), Planned = Math.Round(d.Planned, 2), Variance = Math.Round(d.Variance, 2), Status = d.Status is { } s ? PortfolioService.Camel(s) : null },
                Projects = linked.Select(x => new { x.p.Id, x.p.Code, x.p.NameAr, x.p.Status, x.p.Progress, x.l.Impact }),
            };
        }));
    }

    /// <summary>PMO updates the measured KPI value.</summary>
    [HttpPut("kpis/{id}")]
    public async Task<IActionResult> UpdateKpi(string id, KpiValues body, CancellationToken ct)
    {
        var u = await me.RequireAsync(ct);
        if (u.Role != Role.Pmo) throw new ForbiddenException("Only the PMO edits KPIs.");
        var k = await db.Kpis.FindAsync(new object[] { id }, ct) ?? throw new NotFoundException("KPI not found.");
        (k.Baseline, k.BaselineYear, k.Target, k.TargetYear, k.Current, k.CurrentYear, k.Unit, k.DataOwner) =
            (body.Baseline, body.BaselineYear, body.Target, body.TargetYear, body.Current, body.CurrentYear, body.Unit, body.DataOwner);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Leadership view: approved figures only, no overdue activities for executives, optional sector-lead filter.
    /// </summary>
    [HttpGet("leadership")]
    public async Task<IActionResult> Leadership([FromQuery] string? lead, CancellationToken ct)
    {
        var u = await me.RequirePageAsync("leadership", ct);
        var ps = await portfolio.ProjectsAsync(u, approvedOnly: true, leadFilter: lead, ct: ct);
        var ids = ps.Select(p => p.Id).ToHashSet();
        double W(IEnumerable<ProjectView> xs, Func<ProjectView, double> f)
        {
            var l = xs.ToList();
            var bw = l.Sum(p => (double)p.Budget);
            return l.Count == 0 ? 0 : bw > 0 ? l.Sum(p => f(p) * (double)p.Budget) / bw : l.Average(f);
        }
        var risks = await db.Risks.AsNoTracking().Where(r => ids.Contains(r.ProjectId) && r.Status != "closed")
            .OrderByDescending(r => r.Probability * r.Impact).Take(8).ToListAsync(ct);
        var pending = await db.ChangeRequests.AsNoTracking().CountAsync(c => ids.Contains(c.ProjectId) && (c.Status == ChangeRequestStatus.PmoReview || c.Status == ChangeRequestStatus.WithNpc), ct);
        var closures = await db.Closures.AsNoTracking().CountAsync(c => ids.Contains(c.ProjectId) && c.Status == ClosureStatus.Pending, ct);

        return Ok(new
        {
            Lead = lead,
            Projects = ps.Count,
            Budget = ps.Sum(p => p.Budget),
            Spent = ps.Sum(p => p.ActualSpend),
            Progress = Math.Round(W(ps, p => p.Progress), 2),
            Planned = Math.Round(W(ps, p => p.Planned), 2),
            ByStatus = ps.GroupBy(p => p.Status).ToDictionary(g => g.Key, g => g.Count()),
            ByOutcome = ps.GroupBy(p => p.Portfolio ?? 0).OrderBy(g => g.Key).Select(g => new { Outcome = g.Key, Projects = g.Count(), Budget = g.Sum(p => p.Budget), Progress = Math.Round(W(g, p => p.Progress), 2), Planned = Math.Round(W(g, p => p.Planned), 2) }),
            ByInitiative = ps.GroupBy(p => p.InitiativeCode ?? "—").OrderByDescending(g => g.Sum(p => p.Budget)).Select(g => new { Initiative = g.Key, Projects = g.Count(), Budget = g.Sum(p => p.Budget), Spent = g.Sum(p => p.ActualSpend), Progress = Math.Round(W(g, p => p.Progress), 2) }),
            ByLead = ps.GroupBy(p => p.SectorLead ?? "—").Select(g => new { Lead = g.Key, Projects = g.Count(), Progress = Math.Round(W(g, p => p.Progress), 2), Planned = Math.Round(W(g, p => p.Planned), 2), Delayed = g.Count(p => p.Status == "delayed") }),
            NeedsAttention = ps.Where(p => p.Status is "delayed" or "atRisk").OrderBy(p => p.Variance).Take(8),
            TopRisks = risks.Select(r => new { r.Id, r.ProjectId, r.TitleAr, r.TitleEn, r.Score, r.Owner, r.Due }),
            Decisions = new { ChangeRequests = pending, Closures = closures },
        });
    }

    /// <summary>Status report for a bi-weekly, monthly, quarterly or yearly period (back = 0 is the current one).</summary>
    [HttpGet("reports")]
    public async Task<IActionResult> Report([FromQuery] ReportingFrequency frequency = ReportingFrequency.Biweekly, [FromQuery] int back = 0, CancellationToken ct = default)
    {
        var u = await me.RequirePageAsync("reports", ct);
        var period = ReportingPeriods.Of(frequency, portfolio.Today, Math.Max(0, back));
        var ps = await portfolio.ProjectsAsync(u, approvedOnly: u.Role == Role.Executive, ct: ct);
        var ids = ps.Select(p => p.Id).ToHashSet();
        var from = period.Start.ToDateTime(TimeOnly.MinValue);
        var to = period.End.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var updates = await db.Updates.AsNoTracking().Where(x => ids.Contains(x.ProjectId) && x.SubmittedAt >= from && x.SubmittedAt < to).ToListAsync(ct);
        var crs = await db.ChangeRequests.AsNoTracking().Where(c => ids.Contains(c.ProjectId) && c.SubmittedAt >= from && c.SubmittedAt < to).ToListAsync(ct);
        var closed = await db.Closures.AsNoTracking().Where(c => ids.Contains(c.ProjectId) && c.Status == ClosureStatus.Approved && c.DecidedAt >= from && c.DecidedAt < to).CountAsync(ct);

        return Ok(new
        {
            Period = new { Frequency = PortfolioService.Camel(period.Frequency), period.Start, period.End, period.Key },
            Projects = ps,
            Activity = new
            {
                Updates = updates.Count,
                Approved = updates.Count(x => x.Status == ApprovalStatus.Approved),
                Pending = updates.Count(x => x.Status == ApprovalStatus.Pending),
                Returned = updates.Count(x => x.Status == ApprovalStatus.Returned),
                ProjectsUpdated = updates.Select(x => x.ProjectId).Distinct().Count(),
                ChangeRequests = crs.Count,
                ProjectsClosed = closed,
            },
        });
    }
}

public sealed record KpiValues(decimal? Baseline, string? BaselineYear, decimal? Target, string? TargetYear, decimal? Current, string? CurrentYear, string? Unit, string? DataOwner);
