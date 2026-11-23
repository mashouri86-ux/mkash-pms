using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace MKAsh.Api.Data;

/// <summary>
/// First load of an empty database from database/seed/docs:
///   seed.json   — the 50 PQI projects (plus archived/merged ones) and their work plans
///   access.json — optional list of people and roles (kept out of Git: it holds ministry emails)
/// Any other {collection}.json file (a JSON array of objects with an "id") is loaded into that collection.
/// </summary>
public class Seeder(PmsDbContext db, ILogger<Seeder> log)
{
    public async Task SeedAsync(string dir, CancellationToken ct = default)
    {
        if (await db.Docs.AnyAsync(ct)) { log.LogInformation("Database already has data; seeding skipped."); return; }
        if (!Directory.Exists(dir)) { log.LogWarning("Seed folder {Dir} not found.", dir); return; }

        var now = DateTime.UtcNow;
        var n = 0;
        void Add(string coll, JsonObject o)
        {
            var id = o["id"]?.ToString();
            if (string.IsNullOrEmpty(id)) return;
            o.Remove("id");
            db.Docs.Add(new Doc { Collection = coll, Id = id, Json = o.ToJsonString(), Version = now.Ticks, UpdatedAt = now, UpdatedBy = "seed" });
            n++;
        }

        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.EndsWith(".example", StringComparison.OrdinalIgnoreCase)) continue;
            var root = JsonNode.Parse(await File.ReadAllTextAsync(file, ct));
            if (name == "seed" && root is JsonObject seed)
            {
                foreach (var p in seed["projects"]?.AsArray() ?? new JsonArray()) if (p is JsonObject o) Add("projects", (JsonObject)o.DeepClone());
                foreach (var p in seed["plans"]?.AsArray() ?? new JsonArray()) if (p is JsonObject o) Add("plans", (JsonObject)o.DeepClone());
            }
            else if (root is JsonArray arr && DocsRules.Collections.Contains(name))
            {
                foreach (var x in arr) if (x is JsonObject o) Add(name, (JsonObject)o.DeepClone());
            }
        }
        await db.SaveChangesAsync(ct);
        log.LogInformation("Seeded {Count} records from {Dir}.", n, dir);
    }
}
