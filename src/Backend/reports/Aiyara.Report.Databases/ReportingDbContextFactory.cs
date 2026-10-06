using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aiyara.Report.Databases;

public sealed class ReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ReportingDb")
            ?? throw new InvalidOperationException("Set ConnectionStrings__ReportingDb for design-time operations.");
        var options = new DbContextOptionsBuilder<ReportingDbContext>().UseNpgsql(connectionString).Options;
        return new ReportingDbContext(options);
    }
}
