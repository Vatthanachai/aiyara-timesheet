using System.Security.Cryptography;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Component.Abstractions.Securities.Options;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aiyara.Phase2.Tests;

public sealed class PasetoTokenTests
{
    [Fact]
    public void V4_public_token_round_trips_scoped_claims_and_rejects_tampering()
    {
        var seed = RandomNumberGenerator.GetBytes(32);
        var service = CreateService(seed);
        var claims = new PasetoTokenClaims
        {
            UserId = Guid.NewGuid(), TenantId = Guid.NewGuid(), TokenId = Guid.NewGuid().ToString(),
            Email = "admin@example.test", Role = "TenantAdmin", SessionVersion = 3,
            MustChangePassword = true
        };

        var issued = service.GenerateToken(claims);
        Assert.StartsWith("v4.public.", issued.Value);
        var parsed = Assert.IsType<PasetoTokenClaims>(service.ValidateToken(issued.Value));
        Assert.Equal(claims.UserId, parsed.UserId);
        Assert.Equal(claims.TenantId, parsed.TenantId);
        Assert.Equal(claims.TokenId, parsed.TokenId);
        Assert.Equal(claims.SessionVersion, parsed.SessionVersion);
        Assert.True(parsed.MustChangePassword);
        Assert.Null(CreateService(RandomNumberGenerator.GetBytes(32)).ValidateToken(issued.Value));
        var tamperAt = "v4.public.".Length + 8;
        var tampered = issued.Value[..tamperAt] +
            (issued.Value[tamperAt] == 'A' ? 'B' : 'A') + issued.Value[(tamperAt + 1)..];
        Assert.Null(service.ValidateToken(tampered));
    }

    private static PasetoTokenService CreateService(byte[] seed) => new(Options.Create(new PasetoSetting
    {
        Key = Convert.ToBase64String(seed), Issuer = "test-issuer", Audience = "test-api"
    }));
}
