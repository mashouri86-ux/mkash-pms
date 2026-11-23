namespace MKAsh.Core.Domain;

/// <summary>The four roles. A person holds exactly one.</summary>
public enum Role { Pmo, Executive, Lead, ProjectManager }

/// <summary>Stored project status. Auto means "calculate from progress against plan".</summary>
public enum ProjectStatusOverride { Auto, OnHold, Completed, Closed, Cancelled }

/// <summary>Calculated (or overridden) status shown everywhere.</summary>
public enum ProjectStatus { OnTrack, AtRisk, Delayed, OnHold, Completed, Closed, Cancelled }

public enum ProjectPhase { Initiation, Planning, Execution, Monitoring, Closure }

/// <summary>Project type decides the phase-weight template in the weighted view.</summary>
public enum ProjectType { Study, Policy, Build, Program }

/// <summary>Work-package phase used for weighting.</summary>
public enum WeightPhase { Init, Proc, Exec, Launch, Close }

public enum ActivityStatus { NotStarted, InProgress, Done }

/// <summary>Calculated activity state. Overdue is never stored.</summary>
public enum ActivityState { NotStarted, InProgress, Overdue, Done }

public enum ApprovalStatus { Pending, Approved, Returned }

public enum UpdateKind { Progress, Submit, Risk, Milestone, Evidence }

public enum ChangeRequestStatus { Draft, LeadReview, PmoReview, WithNpc, Approved, Rejected, Returned }

public enum ClosureStatus { Pending, Approved, Returned }

public enum DeliverableAcceptance { Awaiting, Accepted, Rejected }

public enum KpiCategory { Strategic, Ministry }

public enum ProgressMode { PercentComplete, Weighted }

public enum ReportingFrequency { Biweekly, Monthly, Quarterly, Yearly }
