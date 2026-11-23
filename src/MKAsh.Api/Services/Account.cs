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
public class Account(IHttpContextAccessor http, PmsDbContext db, IConfiguration cfg, IHostEnvironment env)
{
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
            var doc = await db.Docs.FirstOrDefaultAsync(d => d.Collection == "_system" && d.Id == OwnersDocId, ct);
            if (doc is null)
            {
                var now = DateTime.UtcNow;
                db.Docs.Add(new Doc
                {
                    Collection = "_system", Id = OwnersDocId, Json = new JsonObject { ["emails"] = new JsonArray(JsonValue.Create(email)) }.ToJsonString(),
                    Version = now.Ticks, UpdatedAt = now, UpdatedBy = email,
                });
                await db.SaveChangesAsync(ct);
                return (_owner = true).Value;
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
}
