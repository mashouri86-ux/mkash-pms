using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using MKAsh.Api.Services;

namespace MKAsh.Api.Data;

/// <summary>
/// The PMO's governance rules (approved November 2026), checked by the server on every save so they hold
/// whatever screen or tool sends the change:
///  6. people change only the projects they manage, lead or cover for (PMO: all);
///  7. nobody approves their own update, change request, closure, recovery plan or KPI baseline;
///  8. a locked baseline's planned dates and activities change only by the PMO (after a change request);
///  3. a published reporting cycle is frozen and takes no more submissions;
/// 10. critical risks (score 15 or more) need an owner, a mitigation and a due date;
/// 11. delegations: the PMO delegates anyone's duties; a sector lead may delegate their own approvals;
///  9. a KPI's named owner may update its current value (and nothing else).
/// </summary>
public static class GovernanceRules
{
    /// <summary>Records that belong to one project (by "projectId", or by their own id for projects and plans).</summary>
    public static readonly HashSet<string> ProjectScoped = new(StringComparer.Ordinal)
    {
        "projects", "plans", "risks", "milestones", "evidence", "updates", "changes", "closures", "recovery",
    };

    public sealed record Refusal(int Status, string Error);

    private static string S(JsonNode? n, string k) => n?[k]?.ToString() ?? "";
    private static bool Same(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    private static string? ProjectOf(string collection, string id, JsonNode? n) =>
        n is null ? null : collection is "projects" or "plans" ? id : S(n, "projectId");

    /// <summary>A KPI owner updating only the current value (the KPI collection is otherwise PMO-only).</summary>
    public static bool IsKpiOwnerValueUpdate(JsonObject? old, JsonObject neu, string email)
    {
        if (old is null || !Same(S(old, "ownerEmail"), email) || !Same(S(neu, "ownerEmail"), email)) return false;
        string[] free = { "current", "curYear", "curAt", "curBy", "valueNote", "updatedAt", "by" };
        var a = old.DeepClone().AsObject();
        var b = neu.DeepClone().AsObject();
        foreach (var k in free) { a.Remove(k); b.Remove(k); }
        return JsonNode.DeepEquals(a, b);
    }

    public static async Task<Refusal?> CheckPutAsync(string collection, string id, JsonObject neu, JsonObject? old,
        string email, bool admin, ProjectAccess access, PmsDbContext db, CancellationToken ct)
    {
        // 6. Project scope
        if (ProjectScoped.Contains(collection))
        {
            var pidNew = ProjectOf(collection, id, neu);
            var pidOld = ProjectOf(collection, id, old);
            if (!await access.CanWriteAsync(pidNew, ct) || (pidOld is not null && pidOld != pidNew && !await access.CanWriteAsync(pidOld, ct)))
                return new(403, "You can only change projects you manage, lead or are covering for.");
        }

        // 11. Delegations
        if (collection == "delegations" && !admin)
        {
            var scope = await access.GetAsync(ct);
            var mine = S(neu, "kind") == "lead" && Same(S(neu, "from"), email) && scope.IsLead && (old is null || Same(S(old, "from"), email));
            if (!mine) return new(403, "Only the PMO can delegate a project manager's duties or someone else's approvals.");
        }

        // 3. Published cycles are frozen
        if (collection == "cycles" && old is not null)
            return new(403, "A published cycle is frozen and can't be changed.");

        // 7. No self-approval
        string os = S(old, "status"), ns = S(neu, "status");
        switch (collection)
        {
            case "updates":
                if (old is null && ns == "approved")
                    return new(403, "New updates go to the sector lead or the PMO for approval.");
                if (old is not null && os == "pending" && ns is "approved" or "returned" && Same(S(old, "by"), email))
                    return new(403, "You can't approve or return your own update.");
                break;
            case "changes":
                if (old is not null && os is "lead" or "pmo" && ns != os && ns is "pmo" or "npc" or "returned" && Same(S(old, "createdBy"), email))
                    return new(403, "You can't decide on a change request you raised.");
                break;
            case "closures":
                if (old is not null && os == "pending" && ns is "approved" or "returned" && Same(S(old, "requestedBy"), email))
                    return new(403, "You can't decide on a closure you requested.");
                break;
            case "recovery":
                if (ns is "accepted" or "returned" && os != ns)
                {
                    if (!admin) return new(403, "Only the PMO accepts or returns recovery plans.");
                    if (Same(S(old, "by"), email)) return new(403, "You can't decide on a recovery plan you wrote.");
                }
                break;
            case "kpis":
                if (S(neu["signoff"], "status") == "approved" && S(old?["signoff"], "status") != "approved" && Same(S(neu, "enteredBy"), email))
                    return new(403, "The person who entered a KPI's baseline and target can't sign them off.");
                break;
        }

        // 10. Critical risks
        if (collection == "risks")
        {
            double.TryParse(S(neu, "probability"), out var p);
            double.TryParse(S(neu, "impact"), out var i);
            if (p * i >= 15 && ns != "closed" && (S(neu, "owner").Trim().Length == 0 || S(neu, "mitigation").Trim().Length == 0 || S(neu, "due").Trim().Length == 0))
                return new(400, "Critical risks (score 15 or more) need an owner, a mitigation and a due date.");
        }

        // 8 and 3. Work plans
        if (collection == "plans" && !admin)
        {
            if (!JsonNode.DeepEquals(old?["baseline"], neu["baseline"]) || !JsonNode.DeepEquals(old?["baselineHist"], neu["baselineHist"]))
                return new(403, "Only the PMO can lock or change a baseline.");
            if (!JsonNode.DeepEquals(old?["wpPhase"], neu["wpPhase"]) || !JsonNode.DeepEquals(old?["noEv"], neu["noEv"]))
                return new(403, "Only the PMO can change work-package phases or evidence exemptions.");
            if (old?["baseline"] is JsonObject && !SameSchedule(old, neu))
                return new(403, "The baseline is locked: planned dates and activities change only through an approved change request.");
            var sub = S(neu["submitted"], "period");
            if (sub.Length > 0 && sub != S(old?["submitted"], "period") &&
                await db.Docs.AnyAsync(d => d.Collection == "cycles" && d.Id == sub && !d.Deleted, ct))
                return new(403, "This reporting cycle is published and closed for submissions.");
        }
        return null;
    }

    /// <summary>Same activities with the same planned dates.</summary>
    private static bool SameSchedule(JsonObject old, JsonObject neu)
    {
        static Dictionary<string, (string, string)> Map(JsonNode? acts)
        {
            var m = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            if (acts is JsonArray a) foreach (var x in a) if (x is JsonObject o) m[S(o, "id")] = (S(o, "ps"), S(o, "pe"));
            return m;
        }
        var A = Map(old["acts"]);
        var B = Map(neu["acts"]);
        return A.Count == B.Count && A.All(kv => B.TryGetValue(kv.Key, out var v) && v == kv.Value);
    }

    public static async Task<Refusal?> CheckDeleteAsync(string collection, string id, JsonObject? old, bool owner, ProjectAccess access, CancellationToken ct)
    {
        if (collection == "cycles" && !owner) return new(403, "A published cycle is frozen and can't be deleted.");
        if (ProjectScoped.Contains(collection) && old is not null && !await access.CanWriteAsync(ProjectOf(collection, id, old), ct))
            return new(403, "You can only change projects you manage, lead or are covering for.");
        return null;
    }
}
