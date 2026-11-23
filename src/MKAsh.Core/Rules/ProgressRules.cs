using MKAsh.Core.Domain;

namespace MKAsh.Core.Rules;

/// <summary>One activity as the rules need it (decoupled from EF entities).</summary>
public sealed record ActivityRow(
    string WorkPackage,
    string Title,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    int Progress,
    ActivityStatus Status);

/// <summary>One project as the rules need it.</summary>
public sealed record ProjectRow(
    string Name,
    DateOnly? Start,
    DateOnly? End,
    decimal ManualProgress,
    ProjectStatusOverride StatusOverride,
    ProjectType? Type);

public sealed record ProjectMetrics(
    double Progress,
    double Planned,
    double Variance,
    double? Spi,
    int ActivityCount,
    ProjectStatus Status);

public sealed record WorkPackageWeight(
    string WorkPackage,
    int Activities,
    int SpanDays,
    double Progress,
    double Planned,
    WeightPhase AutoPhase,
    WeightPhase? Override,
    WeightPhase Phase,
    double Weight);

public sealed record WeightedBreakdown(
    IReadOnlyList<WorkPackageWeight> Rows,
    double Progress,
    double Planned,
    int DuplicatesRemoved,
    ProjectType Type);

/// <summary>
/// Progress, planned progress, status and weighting rules.
/// A line-by-line port of the MKAsh PMS prototype so both produce identical figures.
/// </summary>
public static class ProgressRules
{
    public static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

    public static int Days(DateOnly a, DateOnly b) => b.DayNumber - a.DayNumber;

    /// <summary>Planned duration in days, inclusive. Null when a date is missing.</summary>
    public static int? Duration(ActivityRow a) =>
        a.PlannedStart is { } s && a.PlannedEnd is { } e ? Math.Max(1, Days(s, e) + 1) : null;

    /// <summary>Planned % at a date: 0 before start, 100 after end, straight line in between.</summary>
    public static double? PlannedAt(ActivityRow a, DateOnly date)
    {
        if (a.PlannedStart is not { } s || a.PlannedEnd is not { } e) return null;
        if (date >= e) return 100;
        if (date < s) return 0;
        return Clamp(Days(s, date) / (double)Math.Max(1, Days(s, e)) * 100, 0, 100);
    }

    /// <summary>
    /// Done when progress is 100%, or marked done with progress above 0%.
    /// Marked done at 0% counts as NOT done (PMO decision, Nov 2026).
    /// </summary>
    public static ActivityState State(ActivityRow a, DateOnly today)
    {
        var pr = a.Progress;
        if (pr >= 100 || (a.Status == ActivityStatus.Done && pr > 0)) return ActivityState.Done;
        if (a.PlannedEnd is { } e && e < today) return ActivityState.Overdue;
        if (pr > 0 || a.Status == ActivityStatus.InProgress) return ActivityState.InProgress;
        return ActivityState.NotStarted;
    }

    public static ProjectMetrics Metrics(
        ProjectRow p,
        IReadOnlyList<ActivityRow> acts,
        DateOnly today,
        ProgressMode mode,
        IReadOnlyDictionary<string, WeightPhase>? phaseOverrides = null,
        IReadOnlyDictionary<ProjectType, IReadOnlyDictionary<WeightPhase, int>>? templates = null)
    {
        double planned = 0;
        if (p.Start is { } s && p.End is { } e)
        {
            var dur = Math.Max(1, Days(s, e));
            planned = Clamp(Days(s, today) / (double)dur * 100, 0, 100);
        }
        double prog = Clamp((double)p.ManualProgress, 0, 100);

        if (acts.Count > 0)
        {
            WeightedBreakdown? wm = mode == ProgressMode.Weighted
                ? Weighted(p, acts, today, phaseOverrides, templates)
                : null;
            if (wm is not null)
            {
                prog = wm.Progress;
                planned = wm.Planned;
            }
            else
            {
                prog = acts.Average(a => Clamp(a.Progress, 0, 100));
                var pl = acts.Select(a => PlannedAt(a, today)).Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (pl.Count > 0) planned = pl.Average();
            }
        }
        if (p.StatusOverride is ProjectStatusOverride.Completed or ProjectStatusOverride.Closed) planned = 100;

        var variance = prog - planned;
        return new ProjectMetrics(prog, planned, variance, planned > 0 ? prog / planned : null, acts.Count, Status(p, prog, variance));
    }

    public static ProjectStatus Status(ProjectRow p, double progress, double variance) => p.StatusOverride switch
    {
        ProjectStatusOverride.OnHold => ProjectStatus.OnHold,
        ProjectStatusOverride.Completed => ProjectStatus.Completed,
        ProjectStatusOverride.Closed => ProjectStatus.Closed,
        ProjectStatusOverride.Cancelled => ProjectStatus.Cancelled,
        _ => progress >= 99.5 ? ProjectStatus.Completed
            : variance >= -5 ? ProjectStatus.OnTrack
            : variance >= -15 ? ProjectStatus.AtRisk
            : ProjectStatus.Delayed
    };

    /// <summary>Status band for any variance (used for groups and KPIs).</summary>
    public static ProjectStatus Band(double variance) =>
        variance >= -5 ? ProjectStatus.OnTrack : variance >= -15 ? ProjectStatus.AtRisk : ProjectStatus.Delayed;

    /// <summary>
    /// Weighted progress: phase template × planned span.
    /// 1) drop exact duplicates (same title, start, end);
    /// 2) package progress = duration-weighted mean of its activities;
    /// 3) package phase = PMO override or keyword auto-tag (default Execution);
    /// 4) phase weight from the project-type template, rescaled over phases present;
    /// 5) inside a phase, share by span.
    /// </summary>
    public static WeightedBreakdown? Weighted(
        ProjectRow p,
        IReadOnlyList<ActivityRow> acts,
        DateOnly today,
        IReadOnlyDictionary<string, WeightPhase>? overrides = null,
        IReadOnlyDictionary<ProjectType, IReadOnlyDictionary<WeightPhase, int>>? templates = null)
    {
        var seen = new HashSet<string>();
        var list = new List<ActivityRow>();
        var dup = 0;
        foreach (var a in acts)
        {
            var k = $"{a.Title}|{a.PlannedStart}|{a.PlannedEnd}";
            if (!seen.Add(k)) { dup++; continue; }
            list.Add(a);
        }
        if (list.Count == 0) return null;

        var ds = list.Select(Duration).Where(d => d.HasValue).Select(d => d!.Value).OrderBy(d => d).ToList();
        var median = ds.Count > 0 ? ds[ds.Count / 2] : 30;
        var type = p.Type ?? WeightTemplates.DetectType(p.Name);
        var tpl = (templates ?? WeightTemplates.Defaults)[type];

        var groups = new List<(string wp, List<ActivityRow> items)>();
        foreach (var a in list)
        {
            var key = string.IsNullOrEmpty(a.WorkPackage) ? "—" : a.WorkPackage;
            var g = groups.FindIndex(x => x.wp == key);
            if (g < 0) groups.Add((key, new List<ActivityRow> { a }));
            else groups[g].items.Add(a);
        }

        var rows = new List<(string wp, int n, int span, double prog, double plan, WeightPhase auto, WeightPhase? ovr, WeightPhase phase)>();
        foreach (var (wp, items) in groups)
        {
            double w = 0, pr = 0, pv = 0, pw = 0;
            foreach (var a in items)
            {
                var d = Duration(a) ?? median;
                w += d;
                pr += d * Clamp(a.Progress, 0, 100);
                if (PlannedAt(a, today) is { } x) { pv += d * x; pw += d; }
            }
            var starts = items.Where(a => a.PlannedStart.HasValue).Select(a => a.PlannedStart!.Value).ToList();
            var ends = items.Where(a => a.PlannedEnd.HasValue).Select(a => a.PlannedEnd!.Value).ToList();
            var span = starts.Count > 0 && ends.Count > 0 ? Math.Max(1, Days(starts.Min(), ends.Max()) + 1) : median;
            var auto = PhaseKeywords.AutoPhase(wp);
            WeightPhase? ovr = overrides is not null && overrides.TryGetValue(wp, out var o) ? o : null;
            rows.Add((wp, items.Count, span, w > 0 ? pr / w : 0, pw > 0 ? pv / pw : 0, auto, ovr, ovr ?? auto));
        }

        var present = rows.Select(r => r.phase).Distinct().ToList();
        var total = present.Sum(ph => tpl.TryGetValue(ph, out var v) ? v : 0);
        if (total == 0) return null;

        double prog = 0, planned = 0;
        var result = new List<WorkPackageWeight>();
        var spanByPhase = rows.GroupBy(r => r.phase).ToDictionary(g => g.Key, g => g.Sum(r => r.span));
        foreach (var r in rows)
        {
            var weight = (tpl.TryGetValue(r.phase, out var tw) ? tw : 0) / (double)total * r.span / spanByPhase[r.phase];
            prog += weight * r.prog;
            planned += weight * r.plan;
            result.Add(new WorkPackageWeight(r.wp, r.n, r.span, r.prog, r.plan, r.auto, r.ovr, r.phase, weight));
        }
        return new WeightedBreakdown(result, prog, planned, dup, type);
    }
}
