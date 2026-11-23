using MKAsh.Core.Domain;

namespace MKAsh.Core.Rules;

/// <summary>Who is signed in, as the rules need it.</summary>
public sealed record UserContext(int PersonId, Role Role, string? LeadName, IReadOnlySet<int> ManagedProjectIds);

/// <summary>
/// Server-side permissions. The web app hides what a role cannot use, but every API call
/// is checked here too, so hiding is never the only protection.
/// </summary>
public static class AccessRules
{
    /// <summary>Pages each role can open (mirrors the prototype's tab rules).</summary>
    public static readonly IReadOnlyDictionary<string, Role[]> Pages = new Dictionary<string, Role[]>
    {
        ["home"] = new[] { Role.Pmo, Role.Executive, Role.Lead, Role.ProjectManager },
        ["overview"] = new[] { Role.Pmo, Role.Lead, Role.ProjectManager },
        ["leadership"] = new[] { Role.Pmo, Role.Executive, Role.Lead },
        ["kpis"] = new[] { Role.Pmo, Role.Executive },
        ["projects"] = new[] { Role.Pmo, Role.Executive, Role.Lead, Role.ProjectManager },
        ["workplan"] = new[] { Role.Pmo, Role.Lead, Role.ProjectManager },
        ["updates"] = new[] { Role.Pmo, Role.Lead, Role.ProjectManager },
        ["approvals"] = new[] { Role.Pmo, Role.Lead, Role.ProjectManager },
        ["changes"] = new[] { Role.Pmo, Role.ProjectManager },
        ["risks"] = new[] { Role.Pmo, Role.Lead, Role.ProjectManager },
        ["timeline"] = new[] { Role.Pmo, Role.Executive, Role.Lead, Role.ProjectManager },
        ["reports"] = new[] { Role.Pmo, Role.Executive, Role.Lead, Role.ProjectManager },
        ["data"] = new[] { Role.Pmo },
        ["users"] = new[] { Role.Pmo },
    };

    public static bool CanOpen(UserContext u, string page) => Pages.TryGetValue(page, out var r) && r.Contains(u.Role);

    public static bool LeadMatches(string? projectLead, string? leadName) =>
        !string.IsNullOrWhiteSpace(leadName) && string.Equals(projectLead?.Trim(), leadName.Trim(), StringComparison.Ordinal);

    /// <summary>Row scope: PMO and executives see everything; leads their projects; managers the projects they manage.</summary>
    public static bool CanSee(UserContext u, int projectId, string? projectLead) => u.Role switch
    {
        Role.Pmo or Role.Executive => true,
        Role.Lead => LeadMatches(projectLead, u.LeadName),
        Role.ProjectManager => u.ManagedProjectIds.Contains(projectId),
        _ => false
    };

    /// <summary>Who may change plans, risks, milestones and evidence of a project.</summary>
    public static bool CanEditProject(UserContext u, int projectId) =>
        u.Role == Role.Pmo || (u.Role == Role.ProjectManager && u.ManagedProjectIds.Contains(projectId));

    /// <summary>
    /// Approver for a manager's update: the project's sector lead, or the PMO (always; and the only
    /// approver when the project has no sector lead with an account).
    /// </summary>
    public static bool CanApprove(UserContext u, string? projectLead, bool projectHasLeadAccount) =>
        u.Role == Role.Pmo || (u.Role == Role.Lead && projectHasLeadAccount && LeadMatches(projectLead, u.LeadName));

    /// <summary>PMO changes are approved automatically; everyone else's wait for approval.</summary>
    public static ApprovalStatus InitialApproval(UserContext u) => u.Role == Role.Pmo ? ApprovalStatus.Approved : ApprovalStatus.Pending;

    public static bool CanSeeKpis(UserContext u) => u.Role is Role.Pmo or Role.Executive;

    /// <summary>Executives never see overdue-activity counts.</summary>
    public static bool ShowsOverdue(UserContext u) => u.Role != Role.Executive;
}
