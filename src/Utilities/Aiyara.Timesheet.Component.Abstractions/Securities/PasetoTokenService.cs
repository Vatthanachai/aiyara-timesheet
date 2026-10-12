using Aiyara.Timesheet.Component.Abstractions.Securities.Options;
using Microsoft.Extensions.Options;
using Paseto;
using Paseto.Builder;

namespace Aiyara.Timesheet.Component.Abstractions.Securities;

/// <summary>Issues and verifies short-lived Ed25519 PASETO v4.public access tokens.</summary>
public sealed class PasetoTokenService(IOptions<PasetoSetting> settings) : IPasetoTokenService
{
    public PasetoToken GenerateToken(PasetoTokenClaims claims)
    {
        if (claims.UserId == Guid.Empty || claims.TenantId == Guid.Empty ||
            string.IsNullOrWhiteSpace(claims.TokenId))
            throw new ArgumentException("Subject, tenant and token ID are required.", nameof(claims));

        var options = settings.Value;
        var keyPair = CreateKeyPair(options);
        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.AddMinutes(options.ExpireMinutes);
        var token = new PasetoBuilder()
            .Use(ProtocolVersion.V4, Purpose.Public)
            .WithKey(keyPair.SecretKey)
            .Issuer(options.Issuer)
            .Audience(options.Audience)
            .Subject(claims.UserId.ToString())
            .TokenIdentifier(claims.TokenId)
            .IssuedAt(issuedAt)
            .Expiration(expiresAt)
            .AddClaim("tenant_id", claims.TenantId.ToString())
            .AddClaim("email", claims.Email)
            .AddClaim("role", claims.Role)
            .AddClaim("session_version", claims.SessionVersion)
            .AddClaim("must_change_password", claims.MustChangePassword)
            .Encode();
        return new PasetoToken(token, expiresAt);
    }

    public PasetoTokenClaims ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192) return null;
        var options = settings.Value;
        try
        {
            var result = new PasetoBuilder()
                .Use(ProtocolVersion.V4, Purpose.Public)
                .WithKey(CreateKeyPair(options).PublicKey)
                .Decode(token, new PasetoTokenValidationParameters
                {
                    ValidateLifetime = true,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience
                });
            if (!result.IsValid) return null;
            var payload = result.Paseto.Payload;
            if (!Guid.TryParse(Get(payload, "sub"), out var userId) || userId == Guid.Empty ||
                !Guid.TryParse(Get(payload, "tenant_id"), out var tenantId) || tenantId == Guid.Empty ||
                !long.TryParse(Get(payload, "session_version"), out var version) ||
                !DateTimeOffset.TryParse(Get(payload, "exp"), out var expiresAt) ||
                string.IsNullOrWhiteSpace(Get(payload, "jti"))) return null;
            return new PasetoTokenClaims
            {
                UserId = userId,
                TenantId = tenantId,
                TokenId = Get(payload, "jti"),
                Email = Get(payload, "email"),
                Role = Get(payload, "role"),
                SessionVersion = version,
                ExpiresAtUtc = expiresAt,
                MustChangePassword = bool.TryParse(Get(payload, "must_change_password"), out var mustChange) && mustChange
            };
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or
            InvalidOperationException or System.Text.Json.JsonException or PasetoException)
        {
            return null;
        }
    }

    private static Paseto.Cryptography.Key.PasetoAsymmetricKeyPair CreateKeyPair(PasetoSetting options)
    {
        var seed = Convert.FromBase64String(options.Key ?? string.Empty);
        if (seed.Length != 32 || options.ExpireMinutes is < 1 or > 60 ||
            string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience))
            throw new InvalidOperationException("PASETO signing configuration is invalid.");
        return new PasetoBuilder().Use(ProtocolVersion.V4, Purpose.Public)
            .GenerateAsymmetricKeyPair(seed);
    }

    private static string Get(IDictionary<string, object> payload, string key)
        => payload.TryGetValue(key, out var value) ? value?.ToString() : null;
}
