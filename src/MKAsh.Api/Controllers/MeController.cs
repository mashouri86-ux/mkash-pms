using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MKAsh.Api.Data;
using MKAsh.Api.Services;

namespace MKAsh.Api.Controllers;

public sealed record ProfilesBody(string[] Ids);

[ApiController, Route("api")]
public class MeController(PmsDbContext db, Account me) : ControllerBase
{
    /// <summary>The signed-in account. Its email is the user id the dashboard stores in records.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(new
    {
        id = me.Email,
        email = me.Email,
        name = await NameOf(me.Email, ct) ?? me.Name,
        isOwner = await me.IsOwnerAsync(ct),
        canEdit = await me.IsAdminAsync(ct),
        autoSession = true,
    });

    /// <summary>Names for user ids (emails), from the access list.</summary>
    [HttpPost("profiles")]
    public async Task<IActionResult> Profiles(ProfilesBody body, CancellationToken ct)
    {
        var people = await People(ct);
        var result = new Dictionary<string, object>();
        foreach (var id in body.Ids.Distinct())
        {
            var key = id.ToLowerInvariant();
            people.TryGetValue(key, out var name);
            result[id] = new { name = name ?? "", email = key.Contains('@') ? key : "" };
        }
        return Ok(result);
    }

    /// <summary>Find a person by email (used to link project managers).</summary>
    [HttpGet("users/search")]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken ct)
    {
        var key = (q ?? "").Trim().ToLowerInvariant();
        if (!key.Contains('@')) return Ok(Array.Empty<object>());
        var people = await People(ct);
        people.TryGetValue(key, out var name);
        return Ok(new[] { new { id = key, email = key, name = name ?? "" } });
    }

    private async Task<string?> NameOf(string email, CancellationToken ct) => (await People(ct)).GetValueOrDefault(email);

    private Dictionary<string, string>? _people;

    private async Task<Dictionary<string, string>> People(CancellationToken ct)
    {
        if (_people is not null) return _people;
        var rows = await db.Docs.AsNoTracking().Where(d => d.Collection == "access" && !d.Deleted).Select(d => d.Json).ToListAsync(ct);
        _people = new Dictionary<string, string>();
        foreach (var j in rows)
        {
            var n = JsonNode.Parse(j);
            var email = n?["email"]?.ToString().Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(email)) _people[email] = n?["name"]?.ToString() ?? "";
        }
        return _people;
    }
}
