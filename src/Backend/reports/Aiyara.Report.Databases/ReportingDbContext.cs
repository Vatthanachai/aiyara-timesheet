using Aiyara.Report.Models;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Report.Databases;

public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options,
    ReportingTenantScope tenantScope) : DbContext(options)
{
    public DbSet<ReportDefinition> ReportDefinitions => Set<ReportDefinition>();
    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();
    public DbSet<ReportRun> ReportRuns => Set<ReportRun>();
    public DbSet<ReportSnapshot> ReportSnapshots => Set<ReportSnapshot>();
    public DbSet<ReportObject> ReportObjects => Set<ReportObject>();
    public DbSet<ReportRetentionPolicy> RetentionPolicies => Set<ReportRetentionPolicy>();

    public Guid? CurrentTenantId => tenantScope.TenantId;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ValidateWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateWrites()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwnedReportRecord>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached) continue;
            if (tenantScope.TenantId is not { } tenantId || tenantId == Guid.Empty ||
                entry.Entity.TenantId != tenantId)
                throw new InvalidOperationException("Report writes require a matching tenant scope.");
            if (entry.Entity is ReportSnapshot && entry.State != EntityState.Added)
                throw new InvalidOperationException("Report snapshots are append-only.");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReportDefinition>(entity =>
        {
            entity.ToTable("report_definitions");
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.TenantId, x.Id });
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Format).HasConversion<string>().HasMaxLength(10);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.Kind, x.Format }).IsUnique();
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<ReportSchedule>(entity =>
        {
            entity.ToTable("report_schedules");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.ReportDefinitionId }).IsUnique();
            entity.HasOne<ReportDefinition>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.ReportDefinitionId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<ReportRun>(entity =>
        {
            entity.ToTable("report_runs");
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.TenantId, x.Id });
            entity.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.SubjectUserId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAtUtc });
            entity.HasOne<ReportDefinition>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.ReportDefinitionId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<ReportSnapshot>(entity =>
        {
            entity.ToTable("report_snapshots");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.ReportRunId }).IsUnique();
            entity.HasOne<ReportRun>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.ReportRunId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<ReportObject>(entity =>
        {
            entity.ToTable("report_objects");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ObjectKey).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.ObjectKey, x.Version }).IsUnique();
            entity.HasOne<ReportRun>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.ReportRunId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<ReportRetentionPolicy>(entity =>
        {
            entity.ToTable("report_retention_policies");
            entity.HasKey(x => x.TenantId);
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
    }
}
