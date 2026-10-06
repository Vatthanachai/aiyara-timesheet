using Microsoft.EntityFrameworkCore;

namespace Aiyara.Notifications.Databases;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
}
