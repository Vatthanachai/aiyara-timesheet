using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aiyara.Timesheet.Databases;

public sealed class TimesheetDbContextFactory : IDesignTimeDbContextFactory<TimesheetDbContext>
{
    public TimesheetDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__TimesheetDb")
            ?? throw new InvalidOperationException("Set ConnectionStrings__TimesheetDb for design-time operations.");
        var options = new DbContextOptionsBuilder<TimesheetDbContext>().UseNpgsql(connectionString).Options;
        return new TimesheetDbContext(options);
    }
}
