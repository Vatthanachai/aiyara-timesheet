using Aiyara.Identities.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Databases;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, TenantScope tenantScope)
    : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<MemberGroup> MemberGroups => Set<MemberGroup>();
    public DbSet<OnboardingCapability> OnboardingCapabilities => Set<OnboardingCapability>();
    public DbSet<CredentialChallenge> CredentialChallenges => Set<CredentialChallenge>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<PlatformOrganizationAudit> PlatformOrganizationAudits => Set<PlatformOrganizationAudit>();
    public DbSet<MfaRecoveryCode> MfaRecoveryCodes => Set<MfaRecoveryCode>();
    public Guid? CurrentTenantId => tenantScope.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Slug).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.OrganizationCode).HasMaxLength(7).IsRequired();
            entity.HasIndex(x => x.OrganizationCode).IsUnique();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LogoKey).HasMaxLength(200);
            entity.Property(x => x.DateFormat).HasMaxLength(20).IsRequired().HasDefaultValue(OrganizationFormats.DefaultDateFormat);
            entity.Property(x => x.TimeFormat).HasMaxLength(20).IsRequired().HasDefaultValue(OrganizationFormats.DefaultTimeFormat);
            entity.HasQueryFilter(x => CurrentTenantId != null && x.Id == CurrentTenantId);
        });

        modelBuilder.Entity<PlatformOrganizationAudit>(entity =>
        {
            entity.ToTable("platform_organization_audit");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("accounts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PhotoUrl).HasMaxLength(2048);
            entity.Property(x => x.JobTitle).HasMaxLength(160);
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.PasswordHash).HasMaxLength(256);
            entity.Property(x => x.MfaSecretProtected).HasMaxLength(256);
            entity.Property(x => x.MfaPendingSecretProtected).HasMaxLength(256);
        });

        modelBuilder.Entity<MfaRecoveryCode>(entity =>
        {
            entity.ToTable("mfa_recovery_codes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.CodeHash).IsUnique();
            entity.HasIndex(x => x.AccountId);
            entity.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId);
        });

        modelBuilder.Entity<Membership>(entity =>
        {
            entity.ToTable("memberships");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.AccountId }).IsUnique();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            entity.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId);
            entity.HasOne<MemberGroup>().WithMany().HasForeignKey(x => x.MemberGroupId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<MemberGroup>(entity =>
        {
            entity.ToTable("member_groups");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<Invitation>(entity =>
        {
            entity.ToTable("invitations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(40);
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            entity.HasOne<Account>().WithMany().HasForeignKey(x => x.InvitedByAccountId);
            entity.HasOne<MemberGroup>().WithMany().HasForeignKey(x => x.MemberGroupId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<OnboardingCapability>(entity =>
        {
            entity.ToTable("onboarding_capabilities");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.KeyHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.KeyHash).IsUnique();
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            entity.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId);
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<CredentialChallenge>(entity =>
        {
            entity.ToTable("credential_challenges");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(x => new { x.TenantId, x.AccountId, x.Purpose })
                .IsUnique().HasFilter("\"UsedAtUtc\" IS NULL");
            entity.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId);
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<RefreshSession>(entity =>
        {
            entity.ToTable("refresh_sessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId);
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
    }
}
