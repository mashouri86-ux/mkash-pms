using Microsoft.EntityFrameworkCore;

namespace MKAsh.Api.Data;

/// <summary>
/// One record of a dashboard collection (projects, plans, risks, milestones, evidence, updates,
/// changes, kpis, closures, roles, access, directory, settings). The JSON keeps exactly the fields
/// the dashboard uses, so the screens behave as they did.
/// </summary>
public class Doc
{
    public string Collection { get; set; } = "";
    public string Id { get; set; } = "";
    public string Json { get; set; } = "{}";
    /// <summary>UTC ticks of the last change; drives live updates.</summary>
    public long Version { get; set; }
    public bool Deleted { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>An uploaded file: proof of completion, project charter, closure attachment.</summary>
public class Asset
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string? UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; }
}

public class PmsDbContext(DbContextOptions<PmsDbContext> options) : DbContext(options)
{
    public DbSet<Doc> Docs => Set<Doc>();
    public DbSet<Asset> Assets => Set<Asset>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Doc>(e =>
        {
            e.HasKey(x => new { x.Collection, x.Id });
            e.Property(x => x.Collection).HasMaxLength(40);
            e.Property(x => x.Id).HasMaxLength(200);
            e.Property(x => x.UpdatedBy).HasMaxLength(256);
            e.HasIndex(x => x.Version);
        });
        b.Entity<Asset>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(40);
            e.Property(x => x.FileName).HasMaxLength(400);
            e.Property(x => x.ContentType).HasMaxLength(200);
            e.Property(x => x.UploadedBy).HasMaxLength(256);
        });
    }
}
