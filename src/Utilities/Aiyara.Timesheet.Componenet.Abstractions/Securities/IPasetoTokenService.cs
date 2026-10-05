namespace Aiyara.Timesheet.Component.Abstractions.Securities;

public interface IPasetoTokenService
{
    /// <summary>
    /// Issues a PASETO v4.local access token for the given claims
    /// </summary>
    PasetoToken GenerateToken(PasetoTokenClaims claims);

    /// <summary>
    /// Validates a PASETO v4.local access token and extracts its claims.
    /// Returns null when the token is missing, malformed, or expired.
    /// </summary>
    PasetoTokenClaims ValidateToken(string token);
}
