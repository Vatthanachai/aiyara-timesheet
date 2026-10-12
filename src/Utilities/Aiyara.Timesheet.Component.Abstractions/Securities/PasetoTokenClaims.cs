namespace Aiyara.Timesheet.Component.Abstractions.Securities;

/// <summary>
/// Claims embedded in a PASETO access token
/// </summary>
public class PasetoTokenClaims
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string TokenId { get; set; }
    public long SessionVersion { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }

    public string Email { get; set; }

    public bool MustChangePassword { get; set; }

    public string Role { get; set; }
}

/// <summary>
/// An issued PASETO token and its expiration
/// </summary>
/// <param name="Value">The encoded PASETO token</param>
/// <param name="ExpiresAt">The UTC instant the token expires</param>
public record PasetoToken(string Value, DateTimeOffset ExpiresAt);
