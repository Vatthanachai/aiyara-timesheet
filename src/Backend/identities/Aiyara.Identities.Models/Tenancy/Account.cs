namespace Aiyara.Identities.Models.Tenancy;

public sealed class Account
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public bool IsPlatformAdmin { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
