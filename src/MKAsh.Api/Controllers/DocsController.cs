using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MKAsh.Api.Data;
using MKAsh.Api.Services;

namespace MKAsh.Api.Controllers;

/// <summary>The dashboard's collections, stored in the database.</summary>
[ApiController, Route("api/docs")]
public class DocsController(PmsDbContext db, Account me) : ControllerBase
{
    private static JsonNode? Parse(string json) => JsonNode.Parse(json);

    /// <summary>All current records of one collection.</summary>
    [HttpGet("{collection}")]
    public async Task<IActionResult> List(string collection, CancellationToken ct)
    {
        if (!DocsRules.Collections.Contains(collection)) return NotFound(new { error = "Unknown collection." });
        var now = DateTime.UtcNow.Ticks;
        var rows = await db.Docs.AsNoTracking().Where(d => d.Collection == collection && !d.Deleted).ToListAsync(ct);
        return Ok(new { now, docs = rows.Select(d => new { id = d.Id, data = Parse(d.Json) }) });
    }

    /// <summary>
    /// Everything changed since a point in time (UTC ticks), across collections. Drives live updates.
    /// (Not under /api/docs: "changes" is also the name of the change-requests collection.)
    /// </summary>
    [HttpGet("/api/sync")]
    public async Task<IActionResult> Changes([FromQuery] long since, CancellationToken ct)
    {
        var now = DateTime.UtcNow.Ticks;
        // Look back 5 seconds so writes committed while the last poll ran are not missed (re-applying is harmless).
        var from = Math.Max(0, since - TimeSpan.FromSeconds(5).Ticks);
        var rows = await db.Docs.AsNoTracking().Where(d => d.Version > from && d.Collection != "_system").OrderBy(d => d.Version).Take(5000).ToListAsync(ct);
        return Ok(new
        {
            now,
            changes = rows.Where(d => DocsRules.Collections.Contains(d.Collection))
                .Select(d => new { collection = d.Collection, id = d.Id, deleted = d.Deleted, data = d.Deleted ? null : Parse(d.Json) }),
        });
    }

    [HttpPut("{collection}/{id}")]
    public async Task<IActionResult> Put(string collection, string id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (!DocsRules.Collections.Contains(collection)) return NotFound(new { error = "Unknown collection." });
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200) return BadRequest(new { error = "Bad id." });
        if (body.ValueKind != JsonValueKind.Object) return BadRequest(new { error = "A record must be a JSON object." });
        if (DocsRules.AdminOnly.Contains(collection) && !await me.IsAdminAsync(ct)) return StatusCode(403, new { error = "Only the PMO can change this." });
        var json = body.GetRawText();
        if (Encoding.UTF8.GetByteCount(json) > DocsRules.MaxJsonBytes) return StatusCode(413, new { error = "Record too large." });

        var now = DateTime.UtcNow;
        var doc = await db.Docs.FirstOrDefaultAsync(d => d.Collection == collection && d.Id == id, ct);
        if (doc is null) db.Docs.Add(doc = new Doc { Collection = collection, Id = id });
        doc.Json = json;
        doc.Deleted = false;
        doc.Version = now.Ticks;
        doc.UpdatedAt = now;
        doc.UpdatedBy = me.Email;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{collection}/{id}")]
    public async Task<IActionResult> Delete(string collection, string id, CancellationToken ct)
    {
        if (!DocsRules.Collections.Contains(collection)) return NotFound(new { error = "Unknown collection." });
        if (DocsRules.AdminOnly.Contains(collection) && !await me.IsAdminAsync(ct)) return StatusCode(403, new { error = "Only the PMO can change this." });
        var doc = await db.Docs.FirstOrDefaultAsync(d => d.Collection == collection && d.Id == id, ct);
        if (doc is null) return NoContent();
        // Kept as a tombstone so other open screens learn about the deletion.
        var now = DateTime.UtcNow;
        doc.Deleted = true;
        doc.Json = "{}";
        doc.Version = now.Ticks;
        doc.UpdatedAt = now;
        doc.UpdatedBy = me.Email;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
