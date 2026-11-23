using System.Text.Json;
using MKAsh.Core.Domain;
using MKAsh.Core.Rules;
using Xunit;

namespace MKAsh.Core.Tests;

/// <summary>
/// The system must reproduce the prototype's figures exactly. golden.json was recorded from the
/// prototype with its clock fixed, for all 50 active projects, in both progress views.
/// </summary>
public class GoldenTests : IClassFixture<SeedFixture>
{
    private readonly SeedFixture _f;
    public GoldenTests(SeedFixture f) => _f = f;

    public static IEnumerable<object[]> Modes() => new[] { new object[] { "pct" }, new object[] { "weighted" } };

    [Theory]
    [MemberData(nameof(Modes))]
    public void Project_progress_planned_and_status_match_the_prototype(string mode)
    {
        var expected = _f.Golden.GetProperty(mode);
        var checkedCount = 0;
        foreach (var e in expected.EnumerateObject())
        {
            var (p, acts, ovr) = _f.Projects[e.Name];
            var m = ProgressRules.Metrics(p, acts, _f.Today, mode == "weighted" ? ProgressMode.Weighted : ProgressMode.PercentComplete, ovr);
            Assert.True(Math.Abs(m.Progress - e.Value.GetProperty("prog").GetDouble()) < 1e-6, $"{e.Name} {mode} progress {m.Progress}");
            Assert.True(Math.Abs(m.Planned - e.Value.GetProperty("planned").GetDouble()) < 1e-6, $"{e.Name} {mode} planned {m.Planned}");
            Assert.Equal(e.Value.GetProperty("status").GetString(), Camel(m.Status));
            Assert.Equal(e.Value.GetProperty("acts").GetInt32(), m.ActivityCount);
            checkedCount++;
        }
        Assert.Equal(50, checkedCount);
    }

    [Fact]
    public void Strategic_kpi_delivery_matches_the_prototype()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(root.FullName, "database"))) root = root.Parent!;
        using var kpis = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "database", "seed", "kpis.json")));
        var expected = _f.Golden.GetProperty("kpi");
        foreach (var k in kpis.RootElement.GetProperty("strategic").EnumerateArray())
        {
            var id = k.GetProperty("id").GetString()!;
            var inputs = new List<KpiProjectInput>();
            foreach (var l in k.GetProperty("links").EnumerateArray())
            {
                var code = l[0].GetString()!;
                if (!_f.Projects.TryGetValue(code, out var pr)) continue;
                var m = ProgressRules.Metrics(pr.Project, pr.Acts, _f.Today, ProgressMode.PercentComplete, pr.Overrides);
                inputs.Add(new KpiProjectInput(m.Progress, m.Planned, m.Status, l[1].GetInt32()));
            }
            var d = KpiRules.Delivery(inputs);
            var e = expected.GetProperty(id);
            Assert.Equal(e.GetProperty("n").GetInt32(), d.LinkedProjects);
            if (d.LinkedProjects == 0) { Assert.Null(d.Status); continue; }
            Assert.True(Math.Abs(d.Progress - e.GetProperty("prog").GetDouble()) < 1e-6, $"{id} progress");
            Assert.Equal(e.GetProperty("st").GetString(), Camel(d.Status!.Value));
        }
    }

    private static string Camel(ProjectStatus s) => char.ToLowerInvariant(s.ToString()[0]) + s.ToString()[1..];
}
