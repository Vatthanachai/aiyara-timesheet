using System.Text.Json;

using Aiyara.Timesheet.Component.Abstractions.Securities.Options;

using Microsoft.Extensions.Options;

using Paseto;
using Paseto.Builder;

namespace Aiyara.Timesheet.Component.Abstractions.Securities;

/// <summary>
/// Issues PASETO v4.local (symmetric, encrypted) access tokens
/// </summary>
public class PasetoTokenService(IOptions<PasetoSetting> pasetoSetting) : IPasetoTokenService
{
    public PasetoToken GenerateToken(PasetoTokenClaims claims)
    {
        var key = Convert.FromBase64String(pasetoSetting.Value.Key);
        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.AddMinutes(pasetoSetting.Value.ExpireMinutes);

        var token = new PasetoBuilder()
            .Use(ProtocolVersion.V4, Purpose.Local)
            .WithSharedKey(key)
            .Subject(claims.UserId.ToString())
            .IssuedAt(issuedAt)
            .Expiration(expiresAt)
            .AddClaim("email", claims.Email)
            .AddClaim("security_stamp", claims.SecurityStamp)
            .AddClaim("must_change_password", claims.MustChangePassword)
            .AddClaim("role", claims.Role)
            .Encode();

        return new PasetoToken(token, expiresAt);
    }

    public PasetoTokenClaims ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var key = Convert.FromBase64String(pasetoSetting.Value.Key);

        Paseto.PasetoTokenValidationResult result;
        try
        {
            result = new PasetoBuilder()
                .Use(ProtocolVersion.V4, Purpose.Local)
                .WithSharedKey(key)
                .Decode(token, new Paseto.PasetoTokenValidationParameters { ValidateLifetime = true });
        }
        catch
        {
            return null;
        }

        if (!result.IsValid) return null;

        var payload = result.Paseto.Payload;
        if (!payload.TryGetValue("sub", out var subject) || !int.TryParse(subject?.ToString(), out var userId))
        {
            return null;
        }

        return new PasetoTokenClaims
        {
            UserId = userId,
            Email = payload.TryGetValue("email", out var email) ? email?.ToString() : null,
            SecurityStamp = payload.TryGetValue("security_stamp", out var stamp) ? stamp?.ToString() : null,
            MustChangePassword = payload.TryGetValue("must_change_password", out var mustChangePassword) &&
                                  ToBoolean(mustChangePassword),
            Role = payload.TryGetValue("role", out var role) ? role?.ToString() : null,
        };
    }

    /// <summary>
    /// Paseto's payload dictionary stores claim values as boxed System.Text.Json.JsonElement
    /// (from deserializing the token's JSON payload), not raw CLR types - JsonElement doesn't
    /// implement IConvertible, so Convert.ToBoolean throws InvalidCastException on it.
    /// </summary>
    private static bool ToBoolean(object value)
        => value switch
        {
            bool b => b,
            JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False } e => e.GetBoolean(),
            JsonElement { ValueKind: JsonValueKind.String } e => bool.TryParse(e.GetString(), out var parsed) && parsed,
            _ => Convert.ToBoolean(value),
        };
}
