using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MKAsh.Api.Data;
using MKAsh.Api.Services;

namespace MKAsh.Api.Controllers;

/// <summary>
/// Moves data from the Tamkeen dashboard (or a backup) into the system, and exports a full backup.
/// File format: { "collections": { "projects": [ { "id": "...", ... } ], "access": [ ... ], ... } }.
/// Records are matched by id, so importing the same file twice changes nothing.
/// </summary>
[ApiController, Route("api")]
public class ImportController(PmsDbContext db, Account me) : ControllerBase
{
    [HttpPost("import"), RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Import([FromBody] JsonElement body, CancellationToken ct)
    {
        if (!await me.IsAdminAsync(ct)) return StatusCode(403, new { error = "Only the PMO can import data." });
        if (!body.TryGetProperty("collections", out var colls) || colls.ValueKind != JsonValueKind.Object)
            return BadRequest(new { error = "Expected { \"collections\": { ... } }." });

        var now = DateTime.UtcNow;
        var counts = new Dictionary<string, object>();
        foreach (var c in colls.EnumerateObject())
        {
            if (!DocsRules.Collections.Contains(c.Name) || c.Value.ValueKind != JsonValueKind.Array) { counts[c.Name] = "skipped"; continue; }
            var existing = await db.Docs.Where(d => d.Collection == c.Name).ToDictionaryAsync(d => d.Id, ct);
            int added = 0, updated = 0, same = 0;
            foreach (var item in c.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var idEl) || idEl.GetString() is not { Length: > 0 } id) continue;
                var data = new Dictionary<string, JsonElement>();
                foreach (var p in item.EnumerateObject()) if (p.Name != "id") data[p.Name] = p.Value;
                var json = JsonSerializer.Serialize(data);
                if (existing.TryGetValue(id, out var doc))
                {
                    if (!doc.Deleted && JsonEquivalent(doc.Json, json)) { same++; continue; }
                    updated++;
                }
                else
                {
                    db.Docs.Add(doc = new Doc { Collection = c.Name, Id = id });
                    existing[id] = doc;
                    added++;
                }
                doc.Json = json;
                doc.Deleted = false;
                doc.Version = now.Ticks;
                doc.UpdatedAt = now;
                doc.UpdatedBy = me.Email;
            }
            counts[c.Name] = new { added, updated, unchanged = same };
        }
        await db.SaveChangesAsync(ct);
        return Ok(new { imported = counts });
    }

    /// <summary>Full backup of every collection, in the import format.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        if (!await me.IsAdminAsync(ct)) return StatusCode(403, new { error = "Only the PMO can export data." });
        var rows = await db.Docs.AsNoTracking().Where(d => !d.Deleted && d.Collection != "_system").OrderBy(d => d.Collection).ThenBy(d => d.Id).ToListAsync(ct);
        var collections = DocsRules.Collections.OrderBy(x => x).ToDictionary(c => c, c => rows.Where(r => r.Collection == c).Select(r =>
        {
            var o = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(r.Json) ?? new();
            var withId = new Dictionary<string, object> { ["id"] = r.Id };
            foreach (var kv in o) withId[kv.Key] = kv.Value;
            return withId;
        }).ToList());
        var file = JsonSerializer.SerializeToUtf8Bytes(new { source = "Tamkeen", exportedAt = DateTime.UtcNow, collections });
        return File(file, "application/json", $"tamkeen-backup-{DateTime.UtcNow:yyyy-MM-dd}.json");
    }

    private static bool JsonEquivalent(string a, string b)
    {
        try { return System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(a), System.Text.Json.Nodes.JsonNode.Parse(b)); }
        catch { return false; }
    }
}
