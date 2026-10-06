using Microsoft.EntityFrameworkCore;

namespace Aiyara.Report.Databases;

public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : DbContext(options)
{
}
