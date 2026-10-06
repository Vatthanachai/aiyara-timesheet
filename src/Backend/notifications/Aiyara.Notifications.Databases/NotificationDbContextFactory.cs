using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aiyara.Notifications.Databases;

public sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__NotificationDb")
            ?? throw new InvalidOperationException("Set ConnectionStrings__NotificationDb for design-time operations.");
        var options = new DbContextOptionsBuilder<NotificationDbContext>().UseNpgsql(connectionString).Options;
        return new NotificationDbContext(options);
    }
}
