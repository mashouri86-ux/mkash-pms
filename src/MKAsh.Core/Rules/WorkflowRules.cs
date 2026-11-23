using MKAsh.Core.Domain;

namespace MKAsh.Core.Rules;

/// <summary>Change-request and closure state machines.</summary>
public static class WorkflowRules
{
    /// <summary>
    /// NDS3 change-request code: type code . first two letters of the initiative code . entity code . DDMMYY,
    /// e.g. PT.SD.MSDF.221126. A "-2", "-3"… suffix is added when the code already exists.
    /// </summary>
    public static string ChangeRequestCode(string typeCode, string initiativeCode, string entityCode, DateOnly date, Func<string, bool> exists)
    {
        var init = (initiativeCode ?? "").Trim();
        var baseCode = $"{typeCode}.{(init.Length >= 2 ? init[..2] : init)}.{entityCode}.{date:ddMMyy}";
        var code = baseCode;
        for (var n = 2; exists(code); n++) code = $"{baseCode}-{n}";
        return code;
    }

    /// <summary>Where a submitted change request goes first: the sector lead when the project has one, otherwise the PMO.</summary>
    public static ChangeRequestStatus OnSubmit(bool projectHasLeadAccount) =>
        projectHasLeadAccount ? ChangeRequestStatus.LeadReview : ChangeRequestStatus.PmoReview;

    public static ChangeRequestStatus OnApprove(ChangeRequestStatus s) => s switch
    {
        ChangeRequestStatus.LeadReview => ChangeRequestStatus.PmoReview,
        ChangeRequestStatus.PmoReview => ChangeRequestStatus.WithNpc,
        _ => throw new InvalidOperationException($"A change request in {s} cannot be approved.")
    };

    public static bool CanEdit(ChangeRequestStatus s) => s is ChangeRequestStatus.Draft or ChangeRequestStatus.Returned;

    public static bool CanDecide(UserContext u, ChangeRequestStatus s, string? projectLead, bool projectHasLeadAccount) => s switch
    {
        ChangeRequestStatus.LeadReview => AccessRules.CanApprove(u, projectLead, projectHasLeadAccount),
        ChangeRequestStatus.PmoReview => u.Role == Role.Pmo,
        _ => false
    };

    /// <summary>NPC decision recorded by the PMO: yes → Approved (and the change is applied), no → Rejected, more info → Returned.</summary>
    public static ChangeRequestStatus OnNpcDecision(string decision) => decision switch
    {
        "approved" => ChangeRequestStatus.Approved,
        "rejected" => ChangeRequestStatus.Rejected,
        "moreInfo" => ChangeRequestStatus.Returned,
        _ => throw new ArgumentException("Unknown NPC decision", nameof(decision))
    };

    /// <summary>Change types that update the project automatically when the NPC approves.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> AutoApplyFields = new Dictionary<string, string[]>
    {
        ["PN"] = new[] { "nameAr", "nameEn" },
        ["PD"] = new[] { "description" },
        ["PRR"] = new[] { "manager" },
        ["PL"] = new[] { "initiative" },
        ["PB"] = new[] { "budget" },
        ["PT"] = new[] { "start", "end" },
        ["P"] = new[] { "code", "nameAr", "nameEn", "manager", "initiative", "start", "end", "budget" },
        ["PDe"] = Array.Empty<string>(),
    };

    /// <summary>A project closes only when every deliverable is accepted.</summary>
    public static bool CanApproveClosure(IEnumerable<ClosureDeliverable> deliverables) =>
        deliverables.All(d => d.Acceptance == DeliverableAcceptance.Accepted);
}
