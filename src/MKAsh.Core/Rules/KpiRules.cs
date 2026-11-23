using MKAsh.Core.Domain;

namespace MKAsh.Core.Rules;

public sealed record KpiProjectInput(double Progress, double Planned, ProjectStatus Status, int Impact);

public sealed record KpiDelivery(int LinkedProjects, double Progress, double Planned, double Variance, ProjectStatus? Status);

public static class KpiRules
{
    /// <summary>
    /// Delivery progress of a KPI = impact-weighted progress of its linked projects
    /// (high 3, medium 2, low 1; Ministry KPIs use 1 for all).
    /// Closed or completed projects count as 100%. Status: Completed when all linked
    /// projects are done, otherwise the usual variance bands. Null status = no projects linked.
    /// </summary>
    public static KpiDelivery Delivery(IReadOnlyList<KpiProjectInput> links)
    {
        if (links.Count == 0) return new KpiDelivery(0, 0, 0, 0, null);
        double sw = 0, sp = 0, spl = 0;
        var allDone = true;
        foreach (var l in links)
        {
            var done = l.Status is ProjectStatus.Closed or ProjectStatus.Completed;
            if (!done) allDone = false;
            var pr = done ? 100 : l.Progress;
            var pl = done ? 100 : l.Planned;
            sw += l.Impact;
            sp += l.Impact * pr;
            spl += l.Impact * pl;
        }
        var prog = sp / sw;
        var planned = spl / sw;
        var v = prog - planned;
        return new KpiDelivery(links.Count, prog, planned, v, allDone ? ProjectStatus.Completed : ProgressRules.Band(v));
    }

    /// <summary>Share of the distance from baseline to target achieved (clamped to −100…200%). Null when values are missing.</summary>
    public static double? Achievement(decimal? baseline, decimal? current, decimal? target)
    {
        if (baseline is not { } b || current is not { } c || target is not { } t || t == b) return null;
        return ProgressRules.Clamp((double)((c - b) / (t - b)) * 100, -100, 200);
    }
}
