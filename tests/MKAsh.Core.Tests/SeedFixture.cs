using System.Globalization;
using System.Text.Json;
using MKAsh.Core.Domain;
using MKAsh.Core.Rules;

namespace MKAsh.Core.Tests;

/// <summary>Loads database/seed/portfolio.json and the prototype's golden figures.</summary>
public sealed class SeedFixture
{
    public DateOnly Today { get; }
    public Dictionary<string, (ProjectRow Project, List<ActivityRow> Acts, Dictionary<string, WeightPhase> Overrides)> Projects { get; } = new();
    public JsonElement Golden { get; }

    public SeedFixture()
    {
        var root = FindRepoRoot();
        using var seed = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "database", "seed", "portfolio.json")));
        Golden = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "golden.json"))).RootElement.Clone();
        Today = DateOnly.Parse(Golden.GetProperty("today").GetString()!, CultureInfo.InvariantCulture);

        var plans = new Dictionary<string, JsonElement>();
        foreach (var pl in seed.RootElement.GetProperty("plans").EnumerateArray())
            plans[pl.GetProperty("projectId").GetString()!] = pl.Clone();

        foreach (var p in seed.RootElement.GetProperty("projects").EnumerateArray())
        {
            if (p.TryGetProperty("archived", out var ar) && ar.ValueKind == JsonValueKind.True) continue;
            var id = p.GetProperty("id").GetString()!;
            var row = new ProjectRow(
                Str(p, "nameAr") ?? Str(p, "name") ?? "",
                Date(p, "start"), Date(p, "end"),
                p.TryGetProperty("progress", out var pr) && pr.ValueKind == JsonValueKind.Number ? pr.GetDecimal() : 0,
                ProjectStatusOverride.Auto,
                Str(p, "ptype") switch { "study" => ProjectType.Study, "policy" => ProjectType.Policy, "build" => ProjectType.Build, "program" => ProjectType.Program, _ => null });
            var acts = new List<ActivityRow>();
            var ovr = new Dictionary<string, WeightPhase>();
            if (plans.TryGetValue(id, out var plan))
            {
                foreach (var a in plan.GetProperty("acts").EnumerateArray())
                    acts.Add(new ActivityRow(Str(a, "wp") ?? "", Str(a, "t") ?? "", Date(a, "ps"), Date(a, "pe"),
                        a.TryGetProperty("pr", out var ap) && ap.ValueKind == JsonValueKind.Number ? (int)Math.Round(ap.GetDouble()) : 0,
                        Str(a, "st") switch { "done" => ActivityStatus.Done, "inProgress" => ActivityStatus.InProgress, _ => ActivityStatus.NotStarted }));
                if (plan.TryGetProperty("wpPhase", out var wp) && wp.ValueKind == JsonValueKind.Object)
                    foreach (var kv in wp.EnumerateObject())
                        if (Enum.TryParse<WeightPhase>(kv.Value.GetString(), true, out var ph)) ovr[kv.Name] = ph;
            }
            Projects[p.GetProperty("code").GetString()!] = (row, acts, ovr);
        }
    }

    private static string? Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateOnly? Date(JsonElement e, string k) =>
        Str(e, k) is { Length: >= 10 } s && DateOnly.TryParseExact(s[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "database", "seed"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("database/seed not found above the test output folder");
    }
}
