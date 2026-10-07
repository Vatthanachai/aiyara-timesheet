using System.Security.Cryptography;
using Aiyara.Identities.Databases;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Services.Onboarding;

public static class OrganizationCodeGenerator
{
    public const string PlatformCode = "AIYA001";

    public static string BuildPrefix(string name, string slug)
    {
        var prefix = Normalize(name);
        if (prefix.Length < 4) prefix += Normalize(slug);
        return (prefix + "XXXX")[..4];
    }

    public static async Task<string> GenerateAsync(IdentityDbContext db, string name,
        string slug, CancellationToken cancellationToken)
    {
        var prefix = BuildPrefix(name, slug);
        var firstSuffix = RandomNumberGenerator.GetInt32(0, 1000);
        for (var offset = 0; offset < 1000; offset++)
        {
            var suffix = (firstSuffix + offset) % 1000;
            var candidate = $"{prefix}{suffix:D3}";
            if (candidate == PlatformCode) continue;
            if (!await db.Tenants.IgnoreQueryFilters().AnyAsync(
                    tenant => tenant.OrganizationCode == candidate, cancellationToken))
                return candidate;
        }

        throw new InvalidOperationException($"No organization codes remain for prefix {prefix}.");
    }

    private static string Normalize(string value) => new(value
        .Where(char.IsAsciiLetterOrDigit)
        .Select(char.ToUpperInvariant)
        .ToArray());
}
