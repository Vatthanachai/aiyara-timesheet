using Microsoft.EntityFrameworkCore;

namespace Aiyara.Timesheet.Databases;

public sealed class TimesheetDbContext(DbContextOptions<TimesheetDbContext> options) : DbContext(options)
{
}
