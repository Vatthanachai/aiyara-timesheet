namespace Aiyara.Timesheet.Contracts.Identity.V1;

public static class IdentityCacheKeys
{
    public static string AccountVersion(Guid tenantId, Guid accountId)
        => $"identity:account-version:{tenantId:N}:{accountId:N}";

    public static string TenantPolicyEpoch(Guid tenantId)
        => $"identity:tenant-policy:{tenantId:N}";

    public const string RevocationsChannel = "identity:revocations";
}
