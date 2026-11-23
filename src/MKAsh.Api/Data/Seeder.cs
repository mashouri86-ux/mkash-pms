using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MKAsh.Core.Domain;
using MKAsh.Core.Rules;

namespace MKAsh.Api.Data;

/// <summary>
/// Loads the approved portfolio, KPIs and strategy from database/seed into an empty database.
/// People (with ministry emails) are read from database/seed/people.json, which is kept out of Git.
/// </summary>
public class Seeder(PmsDbContext db, ILogger<Seeder> log)
{
    public async Task SeedAsync(string seedDir, CancellationToken ct = default)
    {
        if (await db.Projects.AnyAsync(ct)) { log.LogInformation("Database already has projects; seeding skipped."); return; }

        using var strategy = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(seedDir, "strategy.json"), ct));
        using var portfolio = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(seedDir, "portfolio.json"), ct));
        using var kpis = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(seedDir, "kpis.json"), ct));

        // Strategy: portfolios (sector outcomes) and initiatives
        var initiatives = new Dictionary<string, Initiative>();
        foreach (var o in strategy.RootElement.GetProperty("outcomes").EnumerateArray())
        {
            var p = new Portfolio { Number = o.GetProperty("number").GetInt32(), NameAr = S(o, "nameAr"), NameEn = S(o, "nameEn") };
            foreach (var i in o.GetProperty("initiatives").EnumerateArray())
            {
                var ini = new Initiative { Code = S(i, "code"), NameAr = S(i, "nameAr"), NameEn = S(i, "nameEn"), Portfolio = p };
                p.Initiatives.Add(ini);
                initiatives[ini.Code] = ini;
            }
            db.Portfolios.Add(p);
        }

        // People (optional file, not in Git)
        var people = new List<Person>();
        // people.json holds real ministry accounts and is kept out of Git; the demo list is used when it is absent.
        var peoplePath = Path.Combine(seedDir, "people.json");
        if (!File.Exists(peoplePath)) peoplePath = Path.Combine(seedDir, "people.example.json");
        if (File.Exists(peoplePath))
        {
            using var pj = JsonDocument.Parse(await File.ReadAllTextAsync(peoplePath, ct));
            foreach (var x in pj.RootElement.EnumerateArray())
            {
                var person = new Person
                {
                    NameAr = S(x, "nameAr"), NameEn = S(x, "nameEn"), Email = S(x, "email").ToLowerInvariant(),
                    Role = Enum.Parse<Role>(S(x, "role"), true), LeadName = Opt(x, "leadName"),
                };
                people.Add(person);
                db.People.Add(person);
            }
        }
        else log.LogWarning("No people file found: no one can sign in until people are added.");

        // Projects, work packages, activities
        var plans = portfolio.RootElement.GetProperty("plans").EnumerateArray().ToDictionary(p => S(p, "projectId"), p => p.Clone());
        var byPrototypeId = new Dictionary<string, Project>();
        var mergedInto = new Dictionary<Project, string>();
        foreach (var x in portfolio.RootElement.GetProperty("projects").EnumerateArray())
        {
            var iniCode = (Opt(x, "initiative") ?? "").Split(' ', 2)[0];
            var managerName = Opt(x, "manager");
            var p = new Project
            {
                Code = S(x, "code"), NameAr = S(x, "nameAr"), NameEn = Opt(x, "name") ?? "", Description = Opt(x, "description"),
                Sector = Opt(x, "department"), Objective = Opt(x, "objective"), SectorLead = Opt(x, "sponsor"),
                ManagerName = managerName, Manager = people.FirstOrDefault(pp => pp.Role == Role.ProjectManager && pp.NameAr == managerName),
                Budget = Dec(x, "budget"), ActualSpend = Dec(x, "actual"), ManualProgress = Dec(x, "progress"),
                Start = Date(x, "start"), End = Date(x, "end"),
                Phase = Opt(x, "phase") switch { "planning" => ProjectPhase.Planning, "execution" => ProjectPhase.Execution, "closure" => ProjectPhase.Closure, "monitoring" => ProjectPhase.Monitoring, _ => ProjectPhase.Initiation },
                Priority = Opt(x, "priority") ?? "medium",
                Type = Opt(x, "ptype") is { } t && Enum.TryParse<ProjectType>(t, true, out var pt) ? pt : null,
                Archived = x.TryGetProperty("archived", out var a) && a.ValueKind == JsonValueKind.True,
                PqiNote = Opt(x, "pqiNote"),
                Initiative = initiatives.GetValueOrDefault(iniCode),
            };
            if (Opt(x, "mergedInto") is { } m) mergedInto[p] = m;
            byPrototypeId[S(x, "id")] = p;

            if (plans.TryGetValue(S(x, "id"), out var plan))
            {
                var overrides = plan.TryGetProperty("wpPhase", out var wpo) && wpo.ValueKind == JsonValueKind.Object
                    ? wpo.EnumerateObject().ToDictionary(k => k.Name, k => k.Value.GetString()) : new Dictionary<string, string?>();
                var order = 0;
                foreach (var act in plan.GetProperty("acts").EnumerateArray())
                {
                    var wpName = Opt(act, "wp") ?? "—";
                    var wp = p.WorkPackages.FirstOrDefault(w => w.Name == wpName);
                    if (wp is null)
                    {
                        wp = new WorkPackage
                        {
                            Name = wpName, Order = ++order,
                            PhaseOverride = overrides.TryGetValue(wpName, out var ph) && Enum.TryParse<WeightPhase>(ph, true, out var wph) ? wph : null,
                        };
                        p.WorkPackages.Add(wp);
                    }
                    var progress = act.TryGetProperty("pr", out var pr) && pr.ValueKind == JsonValueKind.Number ? (int)Math.Round(pr.GetDouble()) : 0;
                    var status = Opt(act, "st") switch { "done" => ActivityStatus.Done, "inProgress" => ActivityStatus.InProgress, _ => ActivityStatus.NotStarted };
                    wp.Activities.Add(new Activity
                    {
                        Title = Opt(act, "t") ?? "", PlannedStart = Date(act, "ps"), PlannedEnd = Date(act, "pe"),
                        ActualStart = Date(act, "as"), ActualEnd = Date(act, "ae"), Progress = progress, Status = status,
                        ApprovedProgress = progress, ApprovedStatus = status, ManagerNote = Opt(act, "n"), PmoNote = Opt(act, "pmo"),
                    });
                }
            }
            db.Projects.Add(p);
        }
        await db.SaveChangesAsync(ct);
        foreach (var (p, target) in mergedInto)
            if (byPrototypeId.TryGetValue(target, out var t)) p.MergedIntoId = t.Id;

        // KPIs
        var byCode = byPrototypeId.Values.Where(p => !p.Archived).ToDictionary(p => p.Code);
        foreach (var (cat, key) in new[] { (KpiCategory.Strategic, "strategic"), (KpiCategory.Ministry, "ministry") })
            foreach (var k in kpis.RootElement.GetProperty(key).EnumerateArray())
            {
                var kpi = new Kpi
                {
                    Id = S(k, "id"), Category = cat, NameAr = S(k, "name"), Outcome = k.GetProperty("outcome").GetInt32(),
                    Objective = Opt(k, "objective"), Unit = Opt(k, "unit"),
                    Target = k.TryGetProperty("target", out var tg) && tg.ValueKind == JsonValueKind.Number ? tg.GetDecimal() : null,
                    TargetYear = k.TryGetProperty("target", out var tg2) && tg2.ValueKind == JsonValueKind.Number ? "2030" : null,
                };
                foreach (var l in k.GetProperty("links").EnumerateArray())
                    if (byCode.TryGetValue(l[0].GetString()!, out var proj))
                        kpi.Links.Add(new KpiLink { ProjectId = proj.Id, Impact = l[1].GetInt32() });
                db.Kpis.Add(kpi);
            }

        foreach (var (type, row) in WeightTemplates.Defaults)
            foreach (var (phase, w) in row)
                db.WeightTemplates.Add(new WeightTemplate { Type = type, Phase = phase, Weight = w });
        db.Settings.Add(new Setting { Key = "updateCycle", Value = nameof(ReportingFrequency.Biweekly) });

        await db.SaveChangesAsync(ct);
        log.LogInformation("Seeded {Projects} projects, {Kpis} KPIs, {People} people.", byPrototypeId.Count, await db.Kpis.CountAsync(ct), people.Count);
    }

    private static string S(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static string? Opt(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
    private static decimal Dec(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0;
    private static DateOnly? Date(JsonElement e, string k) =>
        Opt(e, k) is { Length: >= 10 } s && DateOnly.TryParseExact(s[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
