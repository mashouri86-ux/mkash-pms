using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using MKAsh.Api.Data;

namespace MKAsh.Api.Services;

/// <summary>
/// The signed-in account and what it may do.
/// - Owner: listed in Pms:Owners (or, on a test system, the first person to sign in).
///   Owners have full rights and the dashboard's switch-user screen.
/// - PMO: an "access" or "roles" record with role "pmo".
/// Everyone else is placed by the dashboard from their "access"/"roles" record.
/// </summary>
public class Account(IHttpContextAccessor http, PmsDbContext db, IConfiguration cfg, IHostEnvironment env, IWebHostEnvironment web)
{
    private static HashSet<string>? _roster;
    private static readonly object RosterLock = new();
    public const string OwnersDocId = "_owners";

    public string Email
    {
        get
        {
            var u = http.HttpContext?.User;
            var v = u?.FindFirstValue("preferred_username") ?? u?.FindFirstValue(ClaimTypes.Email)
                    ?? u?.FindFirstValue(ClaimTypes.Upn) ?? u?.FindFirstValue("email") ?? u?.FindFirstValue(ClaimTypes.Name) ?? "";
            return v.Trim().ToLowerInvariant();
        }
    }

    public string Name => http.HttpContext?.User.FindFirstValue("name") ?? Email;

    private bool? _owner, _admin;

    public async Task<bool> IsOwnerAsync(CancellationToken ct = default)
    {
        if (_owner is { } o) return o;
        var email = Email;
        var configured = cfg.GetSection("Pms:Owners").Get<string[]>() ?? Array.Empty<string>();
        if (configured.Any(x => string.Equals(x.Trim(), email, StringComparison.OrdinalIgnoreCase))) return (_owner = true).Value;

        // Test systems: the first person to sign in becomes the owner (stored in _system/_owners).
        if (env.IsDevelopment() && cfg.GetValue("Dev:FirstUserIsOwner", true) && email.Length > 0)
        {
            var doc = await db.Docs.AsNoTracking().FirstOrDefaultAsync(d => d.Collection == "_system" && d.Id == OwnersDocId, ct);
            if (doc is null)
            {
                var now = DateTime.UtcNow;
                var entry = db.Docs.Add(new Doc
                {
                    Collection = "_system", Id = OwnersDocId, Json = new JsonObject { ["emails"] = new JsonArray(JsonValue.Create(email)) }.ToJsonString(),
                    Version = now.Ticks, UpdatedAt = now, UpdatedBy = email,
                });
                try
                {
                    await db.SaveChangesAsync(ct);
                    return (_owner = true).Value;
                }
                catch (DbUpdateException)
                {
                    // Another request recorded the owner at the same moment: use what it stored.
                    entry.State = EntityState.Detached;
                    doc = await db.Docs.AsNoTracking().FirstOrDefaultAsync(d => d.Collection == "_system" && d.Id == OwnersDocId, ct);
                    if (doc is null) throw;
                }
            }
            var emails = JsonNode.Parse(doc.Json)?["emails"]?.AsArray().Select(x => x?.ToString().ToLowerInvariant()) ?? Enumerable.Empty<string?>();
            return (_owner = emails.Contains(email)).Value;
        }
        return (_owner = false).Value;
    }

    /// <summary>Owner, or a person whose access/roles record says PMO.</summary>
    public async Task<bool> IsAdminAsync(CancellationToken ct = default)
    {
        if (_admin is { } a) return a;
        if (await IsOwnerAsync(ct)) return (_admin = true).Value;
        var email = Email;
        var recs = await db.Docs.AsNoTracking()
            .Where(d => !d.Deleted && (d.Collection == "access" || d.Collection == "roles") && d.Json.Contains(email))
            .Select(d => d.Json).ToListAsync(ct);
        var pmo = recs.Select(j => JsonNode.Parse(j)).Any(n =>
            n?["role"]?.ToString() == "pmo" &&
            (string.Equals(n?["email"]?.ToString(), email, StringComparison.OrdinalIgnoreCase) || string.Equals(n?["uid"]?.ToString(), email, StringComparison.OrdinalIgnoreCase)));
        return (_admin = pmo).Value;
    }

    private bool? _registered;

    /// <summary>
    /// Whether this email belongs to someone added to Tamkeen: an owner, anyone on the Users list
    /// (access/roles), the directory, or a project manager in the PM roster. Everyone else gets no data.
    /// </summary>
    public async Task<bool> IsRegisteredAsync(CancellationToken ct = default)
    {
        if (_registered is { } r) return r;
        var email = Email;
        if (email.Length == 0) return (_registered = false).Value;
        if (await IsOwnerAsync(ct)) return (_registered = true).Value;
        if (Roster().Contains(email)) return (_registered = true).Value;
        var recs = await db.Docs.AsNoTracking()
            .Where(d => !d.Deleted && (d.Collection == "access" || d.Collection == "roles" || d.Collection == "directory"))
            .Select(d => d.Json).ToListAsync(ct);
        var found = recs.Select(j => JsonNode.Parse(j)).Any(n =>
            string.Equals(n?["email"]?.ToString()?.Trim(), email, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n?["uid"]?.ToString()?.Trim(), email, StringComparison.OrdinalIgnoreCase));
        return (_registered = found).Value;
    }

    private HashSet<string> Roster()
    {
        if (_roster is not null) return _roster;
        lock (RosterLock)
        {
            if (_roster is not null) return _roster;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var file = Path.Combine(web.WebRootPath ?? "", "data", "pm-roster.json");
                if (File.Exists(file) && JsonNode.Parse(File.ReadAllText(file))?["emails"] is JsonObject emails)
                    foreach (var (_, v) in emails)
                        foreach (var e in (v?.ToString() ?? "").Split(new[] { '\n', '\r', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                            if (e.Contains('@')) set.Add(e.Trim().ToLowerInvariant());
            }
            catch { /* no roster: only the Users list and directory count */ }
            return _roster = set;
        }
    }
}
