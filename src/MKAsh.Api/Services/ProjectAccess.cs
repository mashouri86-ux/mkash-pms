using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using MKAsh.Api.Data;

namespace MKAsh.Api.Services;

/// <summary>
/// Which projects the signed-in person may change, decided by the server (the screens apply the same rules):
/// - PMO and owners: every project ("super users": everything a project manager can do, on all projects);
/// - project managers: the projects they manage (name in the directory or the PM roster, or a role record);
/// - sector leads: the projects in their sector (projects whose owner representative is their lead name);
/// - anyone covering for someone through an active delegation: that person's projects or sector.
/// </summary>
public class ProjectAccess(PmsDbContext db, Account me, IWebHostEnvironment web)
{
    public sealed record Scope(bool Admin, HashSet<string> Projects, bool IsLead);

    private static Dictionary<string, string[]>? _roster; // roster name -> emails
    private static readonly object RosterLock = new();
    private Scope? _scope;

    /// <summary>Today in Qatar (UTC+3), as yyyy-MM-dd, for delegation periods.</summary>
    public static string Today => DateTime.UtcNow.AddHours(3).ToString("yyyy-MM-dd");

    public async Task<Scope> GetAsync(CancellationToken ct = default)
    {
        if (_scope is not null) return _scope;
        if (await me.IsAdminAsync(ct)) return _scope = new Scope(true, new HashSet<string>(), false);
        var email = me.Email;
        bool Me(string? e) => !string.IsNullOrWhiteSpace(e) && string.Equals(e.Trim(), email, StringComparison.OrdinalIgnoreCase);
        static string S(JsonObject n, string k) => n[k]?.ToString() ?? "";

        var rows = await db.Docs.AsNoTracking()
            .Where(d => !d.Deleted && (d.Collection == "projects" || d.Collection == "directory" || d.Collection == "access" || d.Collection == "roles" || d.Collection == "delegations"))
            .Select(d => new { d.Collection, d.Id, d.Json }).ToListAsync(ct);
        var docs = rows.Select(r => (r.Collection, r.Id, Node: JsonNode.Parse(r.Json) as JsonObject)).Where(x => x.Node is not null).ToList();
        var projects = docs.Where(x => x.Collection == "projects").ToList();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        // Project manager: my name(s), matched against each project's manager
        var names = docs.Where(x => x.Collection == "directory" && Me(S(x.Node!, "email"))).Select(x => S(x.Node!, "name")).Where(n => n.Length > 0).ToList();
        names.AddRange(RosterNamesOf(email));
        if (names.Count > 0)
            foreach (var p in projects)
            {
                var mgr = S(p.Node!, "manager");
                if (mgr.Length > 0 && names.Any(n => Names.Match(n, mgr))) ids.Add(p.Id);
            }
        foreach (var r in docs.Where(x => x.Collection == "roles"))
            if ((Me(S(r.Node!, "uid")) || Me(S(r.Node!, "email"))) && S(r.Node!, "role") == "pm" && r.Node!["projects"] is JsonArray a)
                foreach (var v in a) if (v is not null) ids.Add(v.ToString());

        // Sector lead
        var leads = docs.Where(x => x.Collection == "access" && Me(S(x.Node!, "email")) && S(x.Node!, "role") == "lead")
            .Select(x => S(x.Node!, "lead")).Where(l => l.Length > 0).ToList();
        var isLead = leads.Count > 0;

        // Active delegations to me
        var today = Today;
        foreach (var d in docs.Where(x => x.Collection == "delegations"))
        {
            var n = d.Node!;
            if (S(n, "cancelled") == "true" || !Me(S(n, "to"))) continue;
            if (string.CompareOrdinal(S(n, "start"), today) > 0 || string.CompareOrdinal(S(n, "end"), today) < 0) continue;
            if (S(n, "kind") == "lead" && S(n, "lead").Length > 0) leads.Add(S(n, "lead"));
            else if (S(n, "kind") == "pm" && n["projects"] is JsonArray pa)
                foreach (var v in pa) if (v is not null) ids.Add(v.ToString());
        }
        if (leads.Count > 0)
            foreach (var p in projects)
            {
                var sp = S(p.Node!, "sponsor");
                if (sp.Length > 0 && leads.Any(l => Names.Match(sp, l) || sp.Trim() == l.Trim())) ids.Add(p.Id);
            }
        return _scope = new Scope(false, ids, isLead);
    }

    public async Task<bool> CanWriteAsync(string? projectId, CancellationToken ct = default)
    {
        var s = await GetAsync(ct);
        return s.Admin || (!string.IsNullOrEmpty(projectId) && s.Projects.Contains(projectId));
    }

    private IEnumerable<string> RosterNamesOf(string email)
    {
        if (_roster is null)
            lock (RosterLock)
            {
                var map = new Dictionary<string, string[]>();
                try
                {
                    var file = Path.Combine(web.WebRootPath ?? "", "data", "pm-roster.json");
                    if (File.Exists(file) && JsonNode.Parse(File.ReadAllText(file))?["emails"] is JsonObject emails)
                        foreach (var (name, v) in emails)
                            map[name] = (v?.ToString() ?? "").Split(new[] { '\n', '\r', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim().ToLowerInvariant()).ToArray();
                }
                catch { /* no roster */ }
                _roster = map;
            }
        return _roster.Where(kv => kv.Value.Contains(email)).Select(kv => kv.Key);
    }
}
