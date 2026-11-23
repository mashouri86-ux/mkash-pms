using Microsoft.EntityFrameworkCore;
using MKAsh.Core.Domain;

namespace MKAsh.Api.Data;

public class PmsDbContext(DbContextOptions<PmsDbContext> options) : DbContext(options)
{
    public DbSet<Portfolio> Portfolios => Set<Portfolio>();
    public DbSet<Initiative> Initiatives => Set<Initiative>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<WorkPackage> WorkPackages => Set<WorkPackage>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<ProgressUpdate> Updates => Set<ProgressUpdate>();
    public DbSet<Risk> Risks => Set<Risk>();
    public DbSet<Milestone> Milestones => Set<Milestone>();
    public DbSet<Evidence> Evidence => Set<Evidence>();
    public DbSet<ChangeRequest> ChangeRequests => Set<ChangeRequest>();
    public DbSet<Closure> Closures => Set<Closure>();
    public DbSet<ClosureDeliverable> ClosureDeliverables => Set<ClosureDeliverable>();
    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();
    public DbSet<Kpi> Kpis => Set<Kpi>();
    public DbSet<KpiLink> KpiLinks => Set<KpiLink>();
    public DbSet<WeightTemplate> WeightTemplates => Set<WeightTemplate>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Portfolio>().HasIndex(x => x.Number).IsUnique();
        b.Entity<Initiative>().Property(x => x.Code).HasMaxLength(20);
        b.Entity<Initiative>().HasIndex(x => x.Code).IsUnique();

        b.Entity<Person>().Property(x => x.Email).HasMaxLength(256);
        b.Entity<Person>().Property(x => x.LeadName).HasMaxLength(200);
        b.Entity<Person>().HasIndex(x => x.Email).IsUnique();
        b.Entity<Person>().Property(x => x.Role).HasConversion<string>().HasMaxLength(20);

        b.Entity<Project>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.SectorLead).HasMaxLength(200);
            e.Property(x => x.Priority).HasMaxLength(20);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Budget).HasPrecision(18, 2);
            e.Property(x => x.ActualSpend).HasPrecision(18, 2);
            e.Property(x => x.ManualProgress).HasPrecision(5, 2);
            e.Property(x => x.Phase).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.StatusOverride).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.SectorLead);
        });

        b.Entity<WorkPackage>().Property(x => x.PhaseOverride).HasConversion<string>().HasMaxLength(10);
        b.Entity<Activity>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ApprovedStatus).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<ProgressUpdate>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Period).HasMaxLength(40);
            e.HasIndex(x => new { x.ProjectId, x.Status });
        });

        b.Entity<ChangeRequest>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Code).HasMaxLength(60);
            e.HasIndex(x => x.Code).IsUnique().HasFilter("[Code] IS NOT NULL");
            e.HasMany(x => x.History).WithOne().HasForeignKey(x => x.ChangeRequestId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Closure>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasMany(x => x.Deliverables).WithOne().HasForeignKey(x => x.ClosureId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.History).WithOne().HasForeignKey(x => x.ClosureId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ClosureDeliverable>().Property(x => x.Acceptance).HasConversion<string>().HasMaxLength(20);

        b.Entity<Kpi>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(20);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Baseline).HasPrecision(18, 4);
            e.Property(x => x.Target).HasPrecision(18, 4);
            e.Property(x => x.Current).HasPrecision(18, 4);
            e.HasMany(x => x.Links).WithOne().HasForeignKey(x => x.KpiId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<KpiLink>().Property(x => x.KpiId).HasMaxLength(20);
        b.Entity<KpiLink>().HasIndex(x => new { x.KpiId, x.ProjectId }).IsUnique();

        b.Entity<WeightTemplate>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Phase).HasConversion<string>().HasMaxLength(10);
            e.HasIndex(x => new { x.Type, x.Phase }).IsUnique();
        });

        b.Entity<Setting>().HasKey(x => x.Key);
        b.Entity<Setting>().Property(x => x.Key).HasMaxLength(100);
        b.Entity<Risk>().Ignore(x => x.Score);
    }
}
