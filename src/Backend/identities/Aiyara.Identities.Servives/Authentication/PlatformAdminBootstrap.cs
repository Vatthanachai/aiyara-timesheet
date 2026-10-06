using System.Net.Mail;
using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Services.Authentication;

public sealed class PlatformAdminBootstrap(IdentityDbContext db, TenantScope tenantScope)
{
    public static readonly Guid PlatformTenantId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    public async Task EnsureAsync(string? configuredEmail, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuredEmail)) return;
        var email = configuredEmail.Trim().ToLowerInvariant();
        if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            throw new InvalidOperationException("Bootstrap Platform Admin email is invalid.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var tenant = await db.Tenants.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == PlatformTenantId, cancellationToken);
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Id = PlatformTenantId, Name = "Aiyara Platform", Slug = "platform-system",
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Tenants.Add(tenant);
        }
        tenantScope.TenantId = PlatformTenantId;
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Email == email,
            cancellationToken);
        if (account is null)
        {
            account = new Account
            {
                Id = Guid.NewGuid(), Email = email, CreatedAtUtc = DateTime.UtcNow
            };
            db.Accounts.Add(account);
        }
        var membership = await db.Memberships.SingleOrDefaultAsync(x =>
            x.AccountId == account.Id, cancellationToken);
        if (membership is null)
        {
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(), TenantId = PlatformTenantId, AccountId = account.Id,
                Role = TenantRole.PlatformAdmin,
                Status = MembershipStatus.PendingActivation,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        account.IsPlatformAdmin = true;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
