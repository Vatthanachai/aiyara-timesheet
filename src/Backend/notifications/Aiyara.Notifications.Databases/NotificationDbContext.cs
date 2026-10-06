using Microsoft.EntityFrameworkCore;

namespace Aiyara.Notifications.Databases;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<NotificationDelivery> Deliveries => Set<NotificationDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationDelivery>(entity =>
        {
            entity.ToTable("notification_deliveries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RecipientEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Template).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Outcome).HasMaxLength(20).IsRequired();
        });
    }
}
