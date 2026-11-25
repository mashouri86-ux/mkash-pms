namespace MKAsh.Api.Data;

/// <summary>Which collections exist and which only the PMO (or a system owner) may change.</summary>
public static class DocsRules
{
    public static readonly HashSet<string> Collections = new(StringComparer.Ordinal)
    {
        "projects", "risks", "milestones", "plans", "evidence", "roles", "updates",
        "changes", "kpis", "closures", "directory", "settings", "access",
        "delegations", "cycles", "recovery",
    };

    /// <summary>Same as the dashboard's rules: user lists, roles, settings and KPI values are PMO-only.</summary>
    public static readonly HashSet<string> AdminOnly = new(StringComparer.Ordinal)
    {
        "roles", "settings", "directory", "access", "kpis", "cycles",
    };

    /// <summary>Largest JSON accepted for one record (a work plan can be a few hundred KB).</summary>
    public const int MaxJsonBytes = 2 * 1024 * 1024;
}
