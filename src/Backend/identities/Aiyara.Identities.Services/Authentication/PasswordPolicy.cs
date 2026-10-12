using Aiyara.Identities.Models.Tenancy;
using System.Text;

namespace Aiyara.Identities.Services.Authentication;

public static class PasswordPolicy
{
    public static bool IsSatisfied(Tenant tenant, string? password)
    {
        if (password is null || password.Length < tenant.PasswordMinimumLength ||
            Encoding.UTF8.GetByteCount(password) > 1024) return false;
        return (!tenant.PasswordRequireUppercase || password.Any(char.IsUpper)) &&
            (!tenant.PasswordRequireLowercase || password.Any(char.IsLower)) &&
            (!tenant.PasswordRequireDigit || password.Any(char.IsDigit)) &&
            (!tenant.PasswordRequireSymbol || password.Any(c => !char.IsLetterOrDigit(c)));
    }

    public static void ValidateSettings(Tenant tenant)
    {
        if (tenant.PasswordMinimumLength is < 12 or > 128 ||
            tenant.PasswordExpiryDays is < 1 or > 3650)
            throw new AuthenticationException(AuthenticationFailure.InvalidInput,
                "Invalid password policy.");
    }
}
