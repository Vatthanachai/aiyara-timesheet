using Microsoft.EntityFrameworkCore;

namespace Aiyara.Timesheet.Databases;

public sealed class TimesheetDbContext(DbContextOptions<TimesheetDbContext> options,
    TimesheetTenantScope scope) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<PersonalTask> PersonalTasks => Set<PersonalTask>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<LeaveEntry> LeaveEntries => Set<LeaveEntry>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<MonthLock> MonthLocks => Set<MonthLock>();
    public DbSet<TimesheetAudit> Audits => Set<TimesheetAudit>();
    public DbSet<TimesheetOutboxEvent> OutboxEvents => Set<TimesheetOutboxEvent>();
    public DbSet<MonthSnapshot> MonthSnapshots => Set<MonthSnapshot>();
    public Guid? CurrentTenantId => scope.TenantId;

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<TenantRecord>()
            .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (scope.TenantId is null || entry.Entity.TenantId != scope.TenantId)
                throw new InvalidOperationException("Tenant scope is required for timesheet writes.");
        }
        if (ChangeTracker.Entries<MonthSnapshot>().Any(x =>
            x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Month snapshots are immutable.");
        if (ChangeTracker.Entries<TimesheetAudit>().Any(x =>
            x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<MonthLock>().Any(x =>
            x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Audit records and month locks are immutable.");
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Configure<Project>(modelBuilder, "projects");
        Configure<Category>(modelBuilder, "categories");
        Configure<PersonalTask>(modelBuilder, "personal_tasks");
        Configure<Holiday>(modelBuilder, "holidays");
        Configure<LeaveEntry>(modelBuilder, "leave_entries");
        Configure<TimeEntry>(modelBuilder, "time_entries");
        Configure<MonthLock>(modelBuilder, "month_locks");
        Configure<TimesheetAudit>(modelBuilder, "timesheet_audit");
        Configure<TimesheetOutboxEvent>(modelBuilder, "timesheet_outbox");
        modelBuilder.Entity<TimesheetOutboxEvent>().Property(x => x.TimeZoneId)
            .HasMaxLength(100).IsRequired();
        Configure<MonthSnapshot>(modelBuilder, "month_snapshots");
        modelBuilder.Entity<MonthSnapshot>().HasIndex(x => new { x.TenantId, x.OwnerId, x.Year, x.Month }).IsUnique();
        modelBuilder.Entity<MonthLock>().HasIndex(x => new { x.TenantId, x.Year, x.Month }).IsUnique();
        modelBuilder.Entity<TimeEntry>().HasIndex(x => new { x.TenantId, x.OwnerId, x.Date });
        modelBuilder.Entity<LeaveEntry>().HasIndex(x => new { x.TenantId, x.OwnerId, x.Date });
        modelBuilder.Entity<PersonalTask>().HasIndex(x => new { x.TenantId, x.OwnerId });
        modelBuilder.Entity<Holiday>().HasIndex(x => new { x.TenantId, x.Date });
        modelBuilder.Entity<Project>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<Category>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<PersonalTask>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<PersonalTask>().Property(x => x.Status).HasMaxLength(20);
        modelBuilder.Entity<Holiday>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<LeaveEntry>().Property(x => x.Kind).HasMaxLength(60);
        modelBuilder.Entity<TimeEntry>().Property(x => x.TaskName).HasMaxLength(200);
        modelBuilder.Entity<TimesheetAudit>().Property(x => x.EntityType).HasMaxLength(60);
        modelBuilder.Entity<TimesheetAudit>().Property(x => x.Action).HasMaxLength(30);
        modelBuilder.Entity<TimesheetOutboxEvent>().Property(x => x.EventType).HasMaxLength(80);
    }

    private void Configure<TEntity>(ModelBuilder modelBuilder, string table) where TEntity : TenantRecord
    {
        modelBuilder.Entity<TEntity>(entity =>
        {
            entity.ToTable(table);
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TenantId);
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
    }
}

public sealed class TimesheetTenantScope
{
    public Guid? TenantId { get; set; }
}
