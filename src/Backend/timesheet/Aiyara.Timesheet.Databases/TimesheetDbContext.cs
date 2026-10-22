using Microsoft.EntityFrameworkCore;

namespace Aiyara.Timesheet.Databases;

public sealed class TimesheetDbContext(DbContextOptions<TimesheetDbContext> options,
    TimesheetTenantScope scope) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<PersonalTask> PersonalTasks => Set<PersonalTask>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<WorkWeek> WorkWeeks => Set<WorkWeek>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveEntry> LeaveEntries => Set<LeaveEntry>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<EntryTemplate> EntryTemplates => Set<EntryTemplate>();
    public DbSet<MonthLock> MonthLocks => Set<MonthLock>();
    public DbSet<MonthApproval> MonthApprovals => Set<MonthApproval>();
    public DbSet<ReminderSettings> ReminderSettings => Set<ReminderSettings>();
    public DbSet<ReminderDelivery> ReminderDeliveries => Set<ReminderDelivery>();
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
        Configure<WorkWeek>(modelBuilder, "work_weeks");
        modelBuilder.Entity<WorkWeek>().HasIndex(x => x.TenantId).IsUnique();
        Configure<LeaveType>(modelBuilder, "leave_types");
        modelBuilder.Entity<LeaveType>().Property(x => x.Name).HasMaxLength(60);
        modelBuilder.Entity<LeaveType>().Property(x => x.AnnualDays).HasPrecision(5, 1);
        Configure<LeaveEntry>(modelBuilder, "leave_entries");
        Configure<TimeEntry>(modelBuilder, "time_entries");
        Configure<EntryTemplate>(modelBuilder, "entry_templates");
        modelBuilder.Entity<EntryTemplate>().HasIndex(x => new { x.TenantId, x.OwnerId });
        // A deleted template's name may be used again; the filter reads the same on PostgreSQL and SQLite.
        modelBuilder.Entity<EntryTemplate>().HasIndex(x => new { x.TenantId, x.OwnerId, x.NormalizedName })
            .IsUnique().HasFilter("\"IsDeleted\" = false");
        modelBuilder.Entity<EntryTemplate>().Property(x => x.Name).HasMaxLength(100);
        modelBuilder.Entity<EntryTemplate>().Property(x => x.NormalizedName).HasMaxLength(100);
        modelBuilder.Entity<EntryTemplate>().Property(x => x.TaskName).HasMaxLength(200);
        Configure<MonthLock>(modelBuilder, "month_locks");
        Configure<TimesheetAudit>(modelBuilder, "timesheet_audit");
        Configure<TimesheetOutboxEvent>(modelBuilder, "timesheet_outbox");
        modelBuilder.Entity<TimesheetOutboxEvent>().Property(x => x.TimeZoneId)
            .HasMaxLength(100).IsRequired();
        Configure<MonthSnapshot>(modelBuilder, "month_snapshots");
        modelBuilder.Entity<MonthSnapshot>().HasIndex(x => new { x.TenantId, x.OwnerId, x.Year, x.Month }).IsUnique();
        modelBuilder.Entity<MonthLock>().HasIndex(x => new { x.TenantId, x.Year, x.Month }).IsUnique();
        Configure<MonthApproval>(modelBuilder, "month_approvals");
        modelBuilder.Entity<MonthApproval>().HasIndex(x => new { x.TenantId, x.OwnerId, x.Year, x.Month }).IsUnique();
        modelBuilder.Entity<MonthApproval>().Property(x => x.Status).HasMaxLength(20);
        modelBuilder.Entity<MonthApproval>().Property(x => x.ReturnReason).HasMaxLength(1000);
        Configure<ReminderSettings>(modelBuilder, "reminder_settings");
        modelBuilder.Entity<ReminderSettings>().HasIndex(x => x.TenantId).IsUnique();
        Configure<ReminderDelivery>(modelBuilder, "reminder_deliveries");
        modelBuilder.Entity<ReminderDelivery>().HasIndex(x => new { x.TenantId, x.OwnerId, x.Kind, x.Date }).IsUnique();
        modelBuilder.Entity<ReminderDelivery>().Property(x => x.Kind).HasMaxLength(20);
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
