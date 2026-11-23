using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MKAsh.Api.Data;
using MKAsh.Core.Domain;
using MKAsh.Core.Rules;

namespace MKAsh.Api.Services;

/// <summary>
/// NDS3 change requests: lists from the template, the request code, applying an approved
/// change to the project, and filling the original Excel template for the NPC.
/// FormJson / ApplyJson / NpcJson use the same keys as the prototype (f, ap, npc).
/// </summary>
public class ChangeRequestService(PmsDbContext db, IConfiguration cfg, IWebHostEnvironment env)
{
    public const string LevelProject = "مشروع";
    public const string TypeNewProject = "إضافة مشروع جديد";
    public const string TypeDeleteProject = "حذف مشروع";

    /// <summary>Change type → project fields set automatically when the NPC approves (same as the prototype).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> ApplyFields = new Dictionary<string, string[]>
    {
        ["تغيير اسم المشروع"] = new[] { "nameAr", "name" },
        ["تغيير وصف المشروع"] = new[] { "description" },
        ["تغيير ممثل المشروع"] = new[] { "manager" },
        ["تغيير ربط المشروع بمبادرات أخرى"] = new[] { "initiative" },
        ["تغيير الميزانية المخطط لها للمشروع"] = new[] { "budget" },
        ["تغيير الجدول الزمني للمشروع (بما في ذلك تاريخ البدء والانتهاء)"] = new[] { "start", "end" },
        [TypeNewProject] = new[] { "code", "nameAr", "name", "manager", "initiative", "start", "end", "budget" },
        [TypeDeleteProject] = Array.Empty<string>(),
    };

    private static readonly string[] ImpactKeys = { "budget", "timeline", "scope", "outputs", "other" };

    private string SeedDir => cfg["Seed:Directory"] ?? Path.Combine(env.ContentRootPath, "..", "..", "database", "seed");
    private string TemplatePath => cfg["ChangeRequests:Template"] ?? Path.Combine(env.ContentRootPath, "..", "..", "templates", "Change_Request_Template.xlsm");

    private JsonObject? _lists;
    public JsonObject Lists => _lists ??= JsonNode.Parse(File.ReadAllText(Path.Combine(SeedDir, "cr-lists.json")))!.AsObject();

    private static string S(JsonNode? n, string key) => n is JsonObject o && o[key] is JsonValue v ? (v.ToString() ?? "").Trim() : "";

    /// <summary>Type code (PN, PT, …) for a level + Arabic type label, or null.</summary>
    public string? TypeCode(string level, string type) =>
        Lists["types"]?[level]?.AsArray().FirstOrDefault(r => r?[0]?.GetValue<string>() == type)?[2]?.GetValue<string>();

    public string? EntityCode(string entity) =>
        Lists["entities"]?.AsArray().FirstOrDefault(r => r?[0]?.GetValue<string>() == entity)?[2]?.GetValue<string>();

    /// <summary>Code per the template formula, unique across requests. Empty when required fields are missing.</summary>
    public async Task<string?> CodeAsync(ChangeRequest c, CancellationToken ct)
    {
        var f = JsonNode.Parse(c.FormJson);
        var ty = TypeCode(S(f, "level"), S(f, "type"));
        var en = EntityCode(S(f, "ownerEntity"));
        var init = S(f, "initCode");
        if (ty is null || en is null || init.Length == 0 ||
            !DateOnly.TryParseExact(S(f, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return null;
        var taken = (await db.ChangeRequests.Where(x => x.Id != c.Id && x.Code != null).Select(x => x.Code!).ToListAsync(ct)).ToHashSet();
        return WorkflowRules.ChangeRequestCode(ty, init, en, d, taken.Contains);
    }

    /// <summary>Required fields of sections 1–3 that are still empty (keys of the template).</summary>
    public static List<string> Missing(ChangeRequest c)
    {
        var f = JsonNode.Parse(c.FormJson);
        var req = new List<string> { "date", "requester", "initCode", "initName", "ownerEntity", "level", "type", "current", "proposed", "reasons" };
        if (S(f, "level") == LevelProject) req.AddRange(new[] { "projCode", "projName", "projOwner" });
        if (S(f, "level") == "مؤشر أداء") req.AddRange(new[] { "kpiName", "kpiOwner" });
        if (S(f, "linked") == "نعم") req.Add("linkedCodes");
        return req.Where(k => S(f, k).Length == 0).ToList();
    }

    /// <summary>
    /// Applies an approved change to the project and returns a line per change made.
    /// Changes at initiative or KPI level are recorded only (the PMO applies them by hand).
    /// </summary>
    public async Task<List<string>> ApplyAsync(ChangeRequest c, CancellationToken ct)
    {
        var f = JsonNode.Parse(c.FormJson);
        var lines = new List<string>();
        if (S(f, "level") != LevelProject || !ApplyFields.TryGetValue(S(f, "type"), out var keys)) return lines;
        var p = await db.Projects.FirstAsync(x => x.Id == c.ProjectId, ct);
        var ap = (string.IsNullOrEmpty(c.ApplyJson) ? null : JsonNode.Parse(c.ApplyJson)) as JsonObject ?? new JsonObject();

        if (S(f, "type") == TypeDeleteProject)
        {
            p.Archived = true;
            lines.Add($"Project {p.Code} archived");
        }
        else if (S(f, "type") == TypeNewProject)
        {
            var np = new Project
            {
                Code = S(ap, "code"), NameAr = S(ap, "nameAr"), NameEn = S(ap, "name") is { Length: > 0 } n ? n : S(ap, "nameAr"),
                SectorLead = p.SectorLead, Sector = p.Sector, Objective = p.Objective, InitiativeId = await InitiativeIdAsync(S(ap, "initiative"), ct) ?? p.InitiativeId,
                Budget = decimal.TryParse(S(ap, "budget"), NumberStyles.Number, CultureInfo.InvariantCulture, out var b) ? b : 0,
                Start = Date(S(ap, "start")), End = Date(S(ap, "end")),
            };
            await SetManagerAsync(np, S(ap, "manager"), ct);
            if (np.Code.Length == 0 || await db.Projects.AnyAsync(x => x.Code == np.Code, ct)) throw new ConflictException("The new project code is empty or already used.");
            db.Projects.Add(np);
            lines.Add($"New project {np.Code} {np.NameAr}");
        }
        else
        {
            foreach (var k in keys)
            {
                var v = S(ap, k);
                if (v.Length == 0) continue;
                switch (k)
                {
                    case "nameAr": lines.Add($"nameAr: {p.NameAr} → {v}"); p.NameAr = v; break;
                    case "name": lines.Add($"name: {p.NameEn} → {v}"); p.NameEn = v; break;
                    case "description": lines.Add("description updated"); p.Description = v; break;
                    case "manager": lines.Add($"manager: {p.ManagerName} → {v}"); await SetManagerAsync(p, v, ct); break;
                    case "initiative":
                        if (await InitiativeIdAsync(v, ct) is { } iid) { lines.Add($"initiative → {v}"); p.InitiativeId = iid; }
                        break;
                    case "budget":
                        if (decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var bud)) { lines.Add($"budget: {p.Budget:N0} → {bud:N0}"); p.Budget = bud; }
                        break;
                    case "start": if (Date(v) is { } s) { lines.Add($"start: {p.Start} → {s}"); p.Start = s; } break;
                    case "end": if (Date(v) is { } e) { lines.Add($"end: {p.End} → {e}"); p.End = e; } break;
                }
            }
        }
        c.AppliedAt = DateTime.UtcNow;
        return lines;
    }

    private async Task<int?> InitiativeIdAsync(string v, CancellationToken ct)
    {
        var code = v.Split(' ', 2)[0].Replace("SD.", "");
        return code.Length == 0 ? null : (await db.Initiatives.FirstOrDefaultAsync(i => i.Code == code, ct))?.Id;
    }

    private async Task SetManagerAsync(Project p, string name, CancellationToken ct)
    {
        p.ManagerName = name;
        p.ManagerId = (await db.People.FirstOrDefaultAsync(x => x.Role == Role.ProjectManager && (x.NameAr == name || x.NameEn == name), ct))?.Id;
    }

    private static DateOnly? Date(string s) =>
        DateOnly.TryParseExact(s.Length >= 10 ? s[..10] : s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    // ---------------- Excel template ----------------

    private abstract record CellValue;
    private sealed record TextCell(string V) : CellValue;
    private sealed record NumberCell(double V) : CellValue;
    private sealed record FormulaResult(string V) : CellValue;

    private static int Serial(string iso) => Date(iso) is { } d ? d.DayNumber - new DateOnly(1899, 12, 30).DayNumber : 0;

    /// <summary>Same cell map as the prototype: sheet 4 ("نموذج طلب التغيير") of the NDS3 template.</summary>
    private static Dictionary<string, CellValue> Cells(ChangeRequest c)
    {
        var f = JsonNode.Parse(c.FormJson);
        var npc = string.IsNullOrEmpty(c.NpcJson) ? null : JsonNode.Parse(c.NpcJson);
        var V = new Dictionary<string, CellValue>();
        void T(string r, string v) => V[r] = new TextCell(v);
        V["C11"] = new FormulaResult(c.Code ?? "");
        var ser = Serial(S(f, "date"));
        if (ser > 0) V["C12"] = new NumberCell(ser); else T("C12", "");
        T("C13", S(f, "requester")); T("C14", S(f, "initCode")); T("C15", S(f, "initName")); T("C16", S(f, "ownerEntity"));
        T("C17", S(f, "level")); T("C18", S(f, "type"));
        T("C19", S(f, "linked") is { Length: > 0 } l ? l : "لا");
        T("C20", S(f, "linked") == "نعم" ? S(f, "linkedCodes") : "");
        var pj = S(f, "level") == LevelProject;
        T("E17", pj ? S(f, "projCode") : ""); T("E18", pj ? S(f, "projName") : ""); T("E19", pj ? S(f, "projOwner") : "");
        T("G17", S(f, "kpiCode")); T("G18", S(f, "kpiName")); T("G19", S(f, "kpiOwner"));
        V["B27"] = new FormulaResult(S(f, "type"));
        T("C27", S(f, "current")); T("D27", S(f, "proposed")); T("E27", S(f, "reasons")); T("G27", S(f, "affected"));
        for (var i = 0; i < ImpactKeys.Length; i++)
        {
            var x = f?["impact"]?[ImpactKeys[i]];
            var on = S(x, "on") is { Length: > 0 } o ? o : "لا";
            T($"C{34 + i}", on); T($"D{34 + i}", on == "نعم" ? S(x, "d") : "");
        }
        if (npc is not null)
        {
            var els = npc["elements"]?.AsArray() ?? new JsonArray();
            for (var i = 0; i < Math.Min(5, els.Count); i++)
            {
                var x = els[i]; var r = 46 + i;
                if (new[] { "kind", "code", "raised", "crCode", "reason" }.All(k => S(x, k).Length == 0)) continue;
                T($"B{r}", S(x, "kind")); T($"C{r}", S(x, "code")); T($"D{r}", S(x, "raised")); T($"E{r}", S(x, "crCode")); T($"F{r}", S(x, "reason"));
            }
            var a = npc["approval"];
            if (new[] { "body", "decision", "date", "notes" }.Any(k => S(a, k).Length > 0))
            {
                T("B57", S(a, "body")); T("C57", S(a, "decision"));
                var ad = Serial(S(a, "date"));
                if (ad > 0) V["D57"] = new NumberCell(ad); else T("D57", "");
                T("E57", S(a, "notes"));
            }
            var nt = npc["notify"]?.AsArray() ?? new JsonArray();
            for (var i = 0; i < Math.Min(6, nt.Count); i++)
            {
                var x = nt[i]; var r = 64 + i;
                if (new[] { "entity", "done", "notes" }.All(k => S(x, k).Length == 0)) continue;
                T($"B{r}", S(x, "entity")); T($"C{r}", S(x, "done")); T($"D{r}", S(x, "notes"));
            }
        }
        return V;
    }

    /// <summary>The NDS3 template (.xlsm, macros kept) with this request filled in; Excel recalculates on open.</summary>
    public async Task<byte[]> WorkbookAsync(ChangeRequest c, CancellationToken ct)
    {
        var ms = new MemoryStream();
        await using (var src = File.OpenRead(TemplatePath)) await src.CopyToAsync(ms, ct);
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            await Rewrite(zip, "xl/worksheets/sheet4.xml", xml => Cells(c).Aggregate(xml, (x, kv) => SetCell(x, kv.Key, kv.Value)), ct);
            await Rewrite(zip, "xl/workbook.xml", xml =>
            {
                xml = Regex.Replace(xml, "<calcPr([^>]*?)/>", m => $"<calcPr{Regex.Replace(m.Groups[1].Value, @"\s+fullCalcOnLoad=""[^""]*""", "")} fullCalcOnLoad=\"1\"/>");
                return Regex.Replace(xml, @"activeTab=""\d+""", "activeTab=\"3\"");
            }, ct);
        }
        return ms.ToArray();
    }

    private static async Task Rewrite(ZipArchive zip, string name, Func<string, string> edit, CancellationToken ct)
    {
        var e = zip.GetEntry(name) ?? throw new InvalidOperationException($"{name} missing from the template");
        string xml;
        using (var r = new StreamReader(e.Open(), Encoding.UTF8)) xml = await r.ReadToEndAsync(ct);
        e.Delete();
        var n = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var w = new StreamWriter(n.Open(), new UTF8Encoding(false));
        await w.WriteAsync(edit(xml));
    }

    private static int Col(string r) => r.TakeWhile(char.IsLetter).Aggregate(0, (n, ch) => n * 26 + (ch - 'A' + 1));

    /// <summary>Port of the prototype's xSetCell: replaces or inserts one cell, keeping its style.</summary>
    private static string SetCell(string xml, string cellRef, CellValue val)
    {
        var m = Regex.Match(xml, $"<c r=\"{cellRef}\"((?:\\s+[a-zA-Z:]+=\"[^\"]*\")*)\\s*(?:/>|>([\\s\\S]*?)</c>)");
        var attrs = m.Success ? Regex.Replace(m.Groups[1].Value, "\\s+t=\"[^\"]*\"", "") : "";
        var inner = m.Success ? m.Groups[2].Value : "";
        string cell = val switch
        {
            FormulaResult fr => $"<c r=\"{cellRef}\"{attrs} t=\"str\">{Regex.Match(inner, "<f[\\s\\S]*?</f>|<f[^>]*/>").Value}<v xml:space=\"preserve\">{SecurityElement.Escape(fr.V)}</v></c>",
            NumberCell n => $"<c r=\"{cellRef}\"{attrs}><v>{n.V.ToString(CultureInfo.InvariantCulture)}</v></c>",
            TextCell { V: "" } => $"<c r=\"{cellRef}\"{attrs}/>",
            TextCell t => $"<c r=\"{cellRef}\"{attrs} t=\"inlineStr\"><is><t xml:space=\"preserve\">{SecurityElement.Escape(t.V)}</t></is></c>",
            _ => "",
        };
        if (m.Success) return xml.Remove(m.Index, m.Length).Insert(m.Index, cell);

        var rn = Regex.Match(cellRef, "\\d+$").Value;
        var rm = Regex.Match(xml, $"<row r=\"{rn}\"([^>]*?)(/>|>([\\s\\S]*?)</row>)");
        if (!rm.Success) return xml;
        string row;
        if (rm.Groups[2].Value == "/>") row = $"<row r=\"{rn}\"{rm.Groups[1].Value}>{cell}</row>";
        else
        {
            var body = rm.Groups[3].Value;
            var after = Regex.Matches(body, "<c r=\"([A-Z]+\\d+)\"").FirstOrDefault(x => Col(x.Groups[1].Value) > Col(cellRef));
            body = after is null ? body + cell : body.Insert(after.Index, cell);
            row = $"<row r=\"{rn}\"{rm.Groups[1].Value}>{body}</row>";
        }
        return xml.Remove(rm.Index, rm.Length).Insert(rm.Index, row);
    }
}
