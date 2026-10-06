namespace Aiyara.Notifications.Databases;

public sealed class NotificationDelivery
{
    public Guid Id { get; set; }
    public required string RecipientEmail { get; set; }
    public required string Template { get; set; }
    public required string Outcome { get; set; }
    public DateTime AttemptedAtUtc { get; set; }
}
