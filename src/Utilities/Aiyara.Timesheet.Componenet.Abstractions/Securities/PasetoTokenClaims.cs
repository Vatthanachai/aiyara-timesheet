namespace Aiyara.Timesheet.Component.Abstractions.Securities;

/// <summary>
/// Claims embedded in a PASETO access token
/// </summary>
public class PasetoTokenClaims
{
    public int UserId { get; set; }

    public string Email { get; set; }

    /// <summary>
    /// Mirrors the user's current SecurityStamp, allowing a future validator to reject tokens issued before a password change
    /// </summary>
    public string SecurityStamp { get; set; }

    public bool MustChangePassword { get; set; }

    public string Role { get; set; }
}

/// <summary>
/// An issued PASETO token and its expiration
/// </summary>
/// <param name="Value">The encoded PASETO token</param>
/// <param name="ExpiresAt">The UTC instant the token expires</param>
public record PasetoToken(string Value, DateTimeOffset ExpiresAt);
