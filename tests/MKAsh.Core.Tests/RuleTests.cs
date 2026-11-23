using MKAsh.Core.Domain;
using MKAsh.Core.Rules;
using Xunit;

namespace MKAsh.Core.Tests;

public class RuleTests
{
    private static readonly DateOnly Today = new(2026, 11, 23);
    private static ActivityRow A(int pr, ActivityStatus st = ActivityStatus.NotStarted, string? end = "2027-01-01") =>
        new("WP", "T", new DateOnly(2026, 1, 1), end is null ? null : DateOnly.Parse(end), pr, st);

    [Fact]
    public void Done_at_zero_percent_is_not_done()
    {
        Assert.NotEqual(ActivityState.Done, ProgressRules.State(A(0, ActivityStatus.Done), Today));
        Assert.Equal(ActivityState.Done, ProgressRules.State(A(10, ActivityStatus.Done), Today));
        Assert.Equal(ActivityState.Done, ProgressRules.State(A(100), Today));
    }

    [Fact]
    public void Past_planned_end_and_not_done_is_overdue()
    {
        Assert.Equal(ActivityState.Overdue, ProgressRules.State(A(40, end: "2026-11-01"), Today));
        Assert.Equal(ActivityState.InProgress, ProgressRules.State(A(40), Today));
    }

    [Theory]
    [InlineData(0, 0, ProjectStatus.OnTrack)]
    [InlineData(40, 45, ProjectStatus.OnTrack)]
    [InlineData(40, 46, ProjectStatus.AtRisk)]
    [InlineData(40, 55, ProjectStatus.AtRisk)]
    [InlineData(40, 56, ProjectStatus.Delayed)]
    [InlineData(99.6, 100, ProjectStatus.Completed)]
    public void Status_bands(double prog, double planned, ProjectStatus expected)
    {
        var p = new ProjectRow("x", null, null, 0, ProjectStatusOverride.Auto, null);
        Assert.Equal(expected, ProgressRules.Status(p, prog, prog - planned));
    }

    [Fact]
    public void Closed_override_wins()
    {
        var p = new ProjectRow("x", null, null, 0, ProjectStatusOverride.Closed, null);
        Assert.Equal(ProjectStatus.Closed, ProgressRules.Status(p, 10, -80));
    }

    [Theory]
    [InlineData("وضع سياسات وطنية للتماسك الأسري", ProjectType.Policy)]
    [InlineData("إطلاق حملات توعوية", ProjectType.Program)]
    [InlineData("تأسيس مرصد اجتماعي", ProjectType.Study)]
    [InlineData("تأسيس مركز التربية الوالدية", ProjectType.Build)]
    public void Project_type_detection(string name, ProjectType expected) => Assert.Equal(expected, WeightTemplates.DetectType(name));

    [Theory]
    [InlineData("تشكيل فريق العمل", WeightPhase.Init)]
    [InlineData("طرح المناقصة", WeightPhase.Proc)]
    [InlineData("قياس الأثر والتقييم", WeightPhase.Close)]
    [InlineData("اعتماد الدليل", WeightPhase.Launch)]
    [InlineData("تنفيذ البرنامج", WeightPhase.Exec)]
    public void Phase_keywords(string name, WeightPhase expected) => Assert.Equal(expected, PhaseKeywords.AutoPhase(name));

    [Fact]
    public void Default_templates_total_100()
    {
        foreach (var row in WeightTemplates.Defaults.Values) Assert.True(WeightTemplates.IsValid(row));
    }

    [Fact]
    public void Kpi_counts_closed_projects_as_complete()
    {
        var d = KpiRules.Delivery(new[]
        {
            new KpiProjectInput(20, 50, ProjectStatus.Delayed, 3),
            new KpiProjectInput(60, 60, ProjectStatus.Closed, 1),
        });
        Assert.Equal((3 * 20 + 100) / 4.0, d.Progress, 6);
        Assert.Equal(ProjectStatus.Delayed, d.Status);
    }

    [Fact]
    public void Biweekly_cycle_runs_sunday_to_second_saturday()
    {
        var p = ReportingPeriods.Of(ReportingFrequency.Biweekly, new DateOnly(2026, 11, 23));
        Assert.Equal(new DateOnly(2026, 11, 22), p.Start);
        Assert.Equal(new DateOnly(2026, 12, 5), p.End);
        Assert.Equal(DayOfWeek.Sunday, p.Start.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 12, 6), ReportingPeriods.Next(p).Start);
    }

    [Fact]
    public void Quarter_and_month_and_year()
    {
        var d = new DateOnly(2026, 11, 23);
        Assert.Equal(new DateOnly(2026, 10, 1), ReportingPeriods.Of(ReportingFrequency.Quarterly, d).Start);
        Assert.Equal(new DateOnly(2026, 7, 1), ReportingPeriods.Of(ReportingFrequency.Quarterly, d, 1).Start);
        Assert.Equal("2026-11", ReportingPeriods.Of(ReportingFrequency.Monthly, d).Key);
        Assert.Equal(new DateOnly(2025, 12, 31), ReportingPeriods.Of(ReportingFrequency.Yearly, d, 1).End);
    }

    [Fact]
    public void Change_request_code_matches_the_template_formula_and_stays_unique()
    {
        var taken = new HashSet<string> { "PT.SD.MSDF.221126" };
        var code = WorkflowRules.ChangeRequestCode("PT", "SD.3.3", "MSDF", new DateOnly(2026, 11, 22), taken.Contains);
        Assert.Equal("PT.SD.MSDF.221126-2", code);
    }

    [Fact]
    public void Change_request_route_skips_lead_when_none()
    {
        Assert.Equal(ChangeRequestStatus.LeadReview, WorkflowRules.OnSubmit(true));
        Assert.Equal(ChangeRequestStatus.PmoReview, WorkflowRules.OnSubmit(false));
        Assert.Equal(ChangeRequestStatus.WithNpc, WorkflowRules.OnApprove(ChangeRequestStatus.PmoReview));
    }

    [Fact]
    public void Access_scopes()
    {
        var lead = new UserContext(1, Role.Lead, "فهد الخيارين", new HashSet<int>());
        var pm = new UserContext(2, Role.ProjectManager, null, new HashSet<int> { 7 });
        var exec = new UserContext(3, Role.Executive, null, new HashSet<int>());
        Assert.True(AccessRules.CanSee(lead, 1, "فهد الخيارين"));
        Assert.False(AccessRules.CanSee(lead, 1, "مها العطية"));
        Assert.True(AccessRules.CanEditProject(pm, 7));
        Assert.False(AccessRules.CanEditProject(pm, 8));
        Assert.False(AccessRules.CanSeeKpis(lead));
        Assert.True(AccessRules.CanSeeKpis(exec));
        Assert.False(AccessRules.ShowsOverdue(exec));
        Assert.False(AccessRules.CanOpen(exec, "workplan"));
    }
}
