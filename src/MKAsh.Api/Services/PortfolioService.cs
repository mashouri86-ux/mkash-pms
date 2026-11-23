using Microsoft.EntityFrameworkCore;
using MKAsh.Api.Data;
using MKAsh.Core.Domain;
using MKAsh.Core.Rules;

namespace MKAsh.Api.Services;

/// <summary>A project with its calculated figures.</summary>
public sealed record ProjectView(
    int Id, string Code, string NameAr, string NameEn, string? Sector, string? InitiativeCode, int? Portfolio,
    string? SectorLead, string? ManagerName, decimal Budget, decimal ActualSpend, DateOnly? Start, DateOnly? End,
    string Phase, string Status, double Progress, double Planned, double Variance, int Activities, int? Overdue, bool Closed);

/// <summary>Loads projects and runs the progress rules over them. All pages use this, so figures always agree.</summary>
public class PortfolioService(PmsDbContext db, TimeProvider clock)
{
    public DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<ProgressMode> ModeAsync(CancellationToken ct = default) =>
        (await db.Settings.FindAsync(new object[] { "progressMode" }, ct))?.Value == "weighted" ? ProgressMode.Weighted : ProgressMode.PercentComplete;

    public async Task<IReadOnlyDictionary<ProjectType, IReadOnlyDictionary<WeightPhase, int>>> TemplatesAsync(CancellationToken ct = default)
    {
        var rows = await db.WeightTemplates.AsNoTracking().ToListAsync(ct);
        if (rows.Count == 0) return WeightTemplates.Defaults;
        return rows.GroupBy(r => r.Type).ToDictionary(g => g.Key,
            g => (IReadOnlyDictionary<WeightPhase, int>)g.ToDictionary(r => r.Phase, r => r.Weight));
    }

    /// <summary>
    /// Projects the user may see, with figures. <paramref name="approvedOnly"/> uses approved activity values
    /// (leadership pages); managers and the PMO work on live values.
    /// </summary>
    public async Task<List<ProjectView>> ProjectsAsync(UserContext u, bool approvedOnly = false, string? leadFilter = null, CancellationToken ct = default)
    {
        var projects = await db.Projects.AsNoTracking()
            .Where(p => !p.Archived)
            .Include(p => p.Initiative).ThenInclude(i => i!.Portfolio)
            .Include(p => p.WorkPackages).ThenInclude(w => w.Activities)
            .AsSplitQuery()
            .ToListAsync(ct);
        var mode = await ModeAsync(ct);
        var templates = await TemplatesAsync(ct);
        var today = Today;
        var showOverdue = AccessRules.ShowsOverdue(u);

        return projects
            .Where(p => AccessRules.CanSee(u, p.Id, p.SectorLead))
            .Where(p => leadFilter is null || AccessRules.LeadMatches(p.SectorLead, leadFilter))
            .Select(p => View(p, today, mode, templates, approvedOnly, showOverdue))
            .OrderBy(p => p.Code, StringComparer.Ordinal)
            .ToList();
    }

    public static ProjectRow Row(Project p) => new(p.NameAr, p.Start, p.End, p.ManualProgress, p.StatusOverride, p.Type);

    public static List<ActivityRow> Rows(Project p, bool approvedOnly) =>
        p.WorkPackages.OrderBy(w => w.Order).SelectMany(w => w.Activities.OrderBy(a => a.Id).Select(a => new ActivityRow(
            w.Name, a.Title, a.PlannedStart, a.PlannedEnd,
            approvedOnly ? a.ApprovedProgress : a.Progress,
            approvedOnly ? a.ApprovedStatus : a.Status))).ToList();

    public static Dictionary<string, WeightPhase> Overrides(Project p) =>
        p.WorkPackages.Where(w => w.PhaseOverride.HasValue).GroupBy(w => w.Name).ToDictionary(g => g.Key, g => g.First().PhaseOverride!.Value);

    public static ProjectView View(Project p, DateOnly today, ProgressMode mode,
        IReadOnlyDictionary<ProjectType, IReadOnlyDictionary<WeightPhase, int>> templates, bool approvedOnly, bool showOverdue)
    {
        var acts = Rows(p, approvedOnly);
        var m = ProgressRules.Metrics(Row(p), acts, today, mode, Overrides(p), templates);
        int? overdue = showOverdue ? acts.Count(a => ProgressRules.State(a, today) == ActivityState.Overdue) : null;
        return new ProjectView(p.Id, p.Code, p.NameAr, p.NameEn, p.Sector, p.Initiative?.Code, p.Initiative?.Portfolio?.Number,
            p.SectorLead, p.ManagerName, p.Budget, p.ActualSpend, p.Start, p.End, p.Phase.ToString(), Camel(m.Status),
            Math.Round(m.Progress, 2), Math.Round(m.Planned, 2), Math.Round(m.Variance, 2), m.ActivityCount, overdue,
            p.StatusOverride == ProjectStatusOverride.Closed);
    }

    public static string Camel<T>(T e) where T : struct, Enum { var s = e.ToString(); return char.ToLowerInvariant(s[0]) + s[1..]; }

    /// <summary>Does this project's sector lead have an account? (decides whether the lead or the PMO approves)</summary>
    public async Task<bool> HasLeadAccountAsync(string? sectorLead, CancellationToken ct = default) =>
        !string.IsNullOrWhiteSpace(sectorLead) &&
        await db.People.AnyAsync(x => x.Role == Role.Lead && x.Active && x.LeadName == sectorLead, ct);

    public async Task<Project> LoadVisibleAsync(UserContext u, int id, CancellationToken ct = default)
    {
        var p = await db.Projects.Include(x => x.Initiative).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("Project not found.");
        if (!AccessRules.CanSee(u, p.Id, p.SectorLead)) throw new ForbiddenException("You cannot see this project.");
        return p;
    }
}
