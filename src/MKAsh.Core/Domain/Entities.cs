namespace MKAsh.Core.Domain;

/// <summary>A strategic portfolio (sector outcome 1–6).</summary>
public class Portfolio
{
    public int Id { get; set; }
    public int Number { get; set; }
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public List<Initiative> Initiatives { get; set; } = new();
}

public class Initiative
{
    public int Id { get; set; }
    /// <summary>e.g. "1.1"</summary>
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public int PortfolioId { get; set; }
    public Portfolio? Portfolio { get; set; }
}

/// <summary>A person who can sign in. Linked to an Entra ID account by email.</summary>
public class Person
{
    public int Id { get; set; }
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string Email { get; set; } = "";
    public Role Role { get; set; }
    /// <summary>For sector leads: the national-lead name used on projects (sponsor field).</summary>
    public string? LeadName { get; set; }
    public bool Active { get; set; } = true;
}

public class Project
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string? Description { get; set; }
    public string? Sector { get; set; }
    public int? InitiativeId { get; set; }
    public Initiative? Initiative { get; set; }
    public string? Objective { get; set; }
    /// <summary>National lead (القائد الوطني). Decides which sector lead approves.</summary>
    public string? SectorLead { get; set; }
    public int? ManagerId { get; set; }
    public Person? Manager { get; set; }
    /// <summary>Manager name as written in the portfolio list (kept when no account exists yet).</summary>
    public string? ManagerName { get; set; }
    public decimal Budget { get; set; }
    public decimal ActualSpend { get; set; }
    public DateOnly? Start { get; set; }
    public DateOnly? End { get; set; }
    public ProjectPhase Phase { get; set; } = ProjectPhase.Initiation;
    public string Priority { get; set; } = "medium";
    public ProjectStatusOverride StatusOverride { get; set; } = ProjectStatusOverride.Auto;
    /// <summary>Null = detect from the project name.</summary>
    public ProjectType? Type { get; set; }
    /// <summary>Simple progress used only when the project has no activities.</summary>
    public decimal ManualProgress { get; set; }
    public bool Archived { get; set; }
    public int? MergedIntoId { get; set; }
    public string? PqiNote { get; set; }
    public DateTime? ClosedAt { get; set; }
    public List<WorkPackage> WorkPackages { get; set; } = new();
}

public class WorkPackage
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Name { get; set; } = "";
    public int Order { get; set; }
    /// <summary>PMO override of the auto-detected phase.</summary>
    public WeightPhase? PhaseOverride { get; set; }
    public List<Activity> Activities { get; set; } = new();
}

public class Activity
{
    public int Id { get; set; }
    public int WorkPackageId { get; set; }
    public WorkPackage? WorkPackage { get; set; }
    public string Title { get; set; } = "";
    public DateOnly? PlannedStart { get; set; }
    public DateOnly? PlannedEnd { get; set; }
    public DateOnly? ActualStart { get; set; }
    public DateOnly? ActualEnd { get; set; }
    public int Progress { get; set; }
    public ActivityStatus Status { get; set; }
    public string? ManagerNote { get; set; }
    public string? PmoNote { get; set; }
    /// <summary>Approved values; leadership pages read these, managers see the live ones.</summary>
    public int ApprovedProgress { get; set; }
    public ActivityStatus ApprovedStatus { get; set; }
}

/// <summary>Every change a project manager makes, and each cycle submission. Also the audit trail.</summary>
public class ProgressUpdate
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public UpdateKind Kind { get; set; }
    public string Period { get; set; } = "";
    public string? Action { get; set; }
    public string? Title { get; set; }
    /// <summary>JSON list of {activityId, from, to} changes.</summary>
    public string? ChangesJson { get; set; }
    public string? Note { get; set; }
    public int SubmittedById { get; set; }
    public DateTime SubmittedAt { get; set; }
    public ApprovalStatus Status { get; set; }
    public int? ReviewedById { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public bool AutoApproved { get; set; }
}

public class Risk
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string TitleAr { get; set; } = "";
    public string? TitleEn { get; set; }
    public string? Category { get; set; }
    public int Probability { get; set; }
    public int Impact { get; set; }
    public int Score => Probability * Impact;
    public string? Owner { get; set; }
    public string Status { get; set; } = "open";
    public DateOnly? Due { get; set; }
    public string? Mitigation { get; set; }
}

public class Milestone
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string TitleAr { get; set; } = "";
    public string? TitleEn { get; set; }
    public DateOnly? Due { get; set; }
    public string Status { get; set; } = "notStarted";
}

/// <summary>A file proving an activity is done. The file itself lives in SharePoint / blob storage.</summary>
public class Evidence
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? ActivityId { get; set; }
    public string FileName { get; set; } = "";
    public string StorageUrl { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public int UploadedById { get; set; }
    public DateTime UploadedAt { get; set; }
}

/// <summary>NDS3 change request (template sections 1–6).</summary>
public class ChangeRequest
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public string? Code { get; set; }
    public ChangeRequestStatus Status { get; set; }
    /// <summary>Sections 1–3 as JSON, keyed like the template (1.2 … 3.3).</summary>
    public string FormJson { get; set; } = "{}";
    /// <summary>Structured values applied to the project when the NPC approves.</summary>
    public string? ApplyJson { get; set; }
    /// <summary>Sections 4–6 (recorded by the PMO) as JSON.</summary>
    public string? NpcJson { get; set; }
    public string? CharterUrl { get; set; }
    public string? ReturnNote { get; set; }
    public int CreatedById { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? AppliedAt { get; set; }
    public List<WorkflowEvent> History { get; set; } = new();
}

public class Closure
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public ClosureStatus Status { get; set; }
    public string Summary { get; set; } = "";
    public string? Lessons { get; set; }
    public string? ReturnNote { get; set; }
    public int RequestedById { get; set; }
    public DateTime RequestedAt { get; set; }
    public int? DecidedById { get; set; }
    public DateTime? DecidedAt { get; set; }
    public List<ClosureDeliverable> Deliverables { get; set; } = new();
    public List<WorkflowEvent> History { get; set; } = new();
}

public class ClosureDeliverable
{
    public int Id { get; set; }
    public int ClosureId { get; set; }
    public int WorkPackageId { get; set; }
    public string Name { get; set; } = "";
    public int Progress { get; set; }
    public string? ManagerNote { get; set; }
    public DeliverableAcceptance Acceptance { get; set; }
    public string? AcceptanceNote { get; set; }
}

/// <summary>History line for change requests and closures.</summary>
public class WorkflowEvent
{
    public int Id { get; set; }
    public int? ChangeRequestId { get; set; }
    public int? ClosureId { get; set; }
    public string Action { get; set; } = "";
    public string? Note { get; set; }
    public int ById { get; set; }
    public DateTime At { get; set; }
}

public class Kpi
{
    /// <summary>"SD.6.K.1" for strategic KPIs, "MK.1.4" for Ministry KPIs.</summary>
    public string Id { get; set; } = "";
    public KpiCategory Category { get; set; }
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public int Outcome { get; set; }
    public string? Objective { get; set; }
    public decimal? Baseline { get; set; }
    public string? BaselineYear { get; set; }
    public decimal? Target { get; set; }
    public string? TargetYear { get; set; }
    public decimal? Current { get; set; }
    public string? CurrentYear { get; set; }
    public string? Unit { get; set; }
    public string? DataOwner { get; set; }
    public string? Notes { get; set; }
    public List<KpiLink> Links { get; set; } = new();
}

public class KpiLink
{
    public int Id { get; set; }
    public string KpiId { get; set; } = "";
    public int ProjectId { get; set; }
    /// <summary>3 = high, 2 = medium, 1 = low.</summary>
    public int Impact { get; set; } = 1;
}

/// <summary>Phase weight template row (project type × phase).</summary>
public class WeightTemplate
{
    public int Id { get; set; }
    public ProjectType Type { get; set; }
    public WeightPhase Phase { get; set; }
    public int Weight { get; set; }
}

public class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
