using Aiyara.Timesheet.Api;
using Aiyara.Timesheet.Databases;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aiyara.Phase3.Tests;

public sealed class TimesheetCoreTests
{
    [Theory]
    [InlineData("09:00", "17:30", 510)]
    [InlineData("22:30", "02:00", 210)]
    public void Duration_handles_same_day_and_overnight(string start, string end, int expected) =>
        Assert.Equal(expected, TimesheetRules.DurationMinutes(TimeOnly.Parse(start), TimeOnly.Parse(end)));

    [Fact]
    public void Zero_duration_is_rejected() => Assert.Throws<ArgumentException>(() =>
        TimesheetRules.DurationMinutes(new TimeOnly(9, 0), new TimeOnly(9, 0)));

    [Fact]
    public void Current_month_uses_tenant_calendar()
    {
        var now = new DateTimeOffset(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);
        Assert.True(TimesheetRules.IsCurrentMonth(new DateOnly(2026, 10, 1), "Asia/Bangkok", now));
        Assert.False(TimesheetRules.IsCurrentMonth(new DateOnly(2026, 9, 30), "Asia/Bangkok", now));
    }

    [Fact]
    public async Task Tenant_queries_fail_closed_and_foreign_writes_are_rejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var scope = new TimesheetTenantScope();
        var options = new DbContextOptionsBuilder<TimesheetDbContext>().UseSqlite(connection).Options;
        await using var db = new TimesheetDbContext(options, scope);
        await db.Database.EnsureCreatedAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        db.Projects.Add(new Project { TenantId = first, Name = "first" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        scope.TenantId = first;
        await db.SaveChangesAsync();
        Assert.Single(await db.Projects.ToListAsync());
        scope.TenantId = second;
        Assert.Empty(await db.Projects.ToListAsync());
        db.Projects.Add(new Project { TenantId = second, Name = "second" });
        await db.SaveChangesAsync();
        Assert.Single(await db.Projects.ToListAsync());
        scope.TenantId = null;
        Assert.Empty(await db.Projects.ToListAsync());
        scope.TenantId = first;
        db.Projects.Add(new Project { TenantId = second, Name = "cross tenant" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_month_can_only_be_locked_once_per_tenant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var tenant = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<TimesheetDbContext>().UseSqlite(connection).Options;
        await using var db = new TimesheetDbContext(options, new TimesheetTenantScope { TenantId = tenant });
        await db.Database.EnsureCreatedAsync();
        db.MonthLocks.Add(new MonthLock { TenantId = tenant, Year = 2026, Month = 10 });
        await db.SaveChangesAsync();
        db.MonthLocks.Add(new MonthLock { TenantId = tenant, Year = 2026, Month = 10 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Month_snapshot_cannot_be_changed_after_creation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var tenant = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<TimesheetDbContext>().UseSqlite(connection).Options;
        await using var db = new TimesheetDbContext(options, new TimesheetTenantScope { TenantId = tenant });
        await db.Database.EnsureCreatedAsync();
        var snapshot = new MonthSnapshot { TenantId = tenant, OwnerId = Guid.NewGuid(),
            Year = 2026, Month = 10, PayloadJson = "{}" };
        db.MonthSnapshots.Add(snapshot);
        await db.SaveChangesAsync();
        snapshot.PayloadJson = "{\"changed\":true}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
}
