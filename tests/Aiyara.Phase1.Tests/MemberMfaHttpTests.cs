using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Aiyara.Identities.Api;
using Aiyara.Identities.Api.Controllers;
using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Component.Abstractions.Securities.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aiyara.Phase1.Tests;

/// <summary>The HTTP contract of member MFA: the sign-in answers a client reacts to, the enrollment
/// offer, and the signed-in endpoints, with their status codes and error codes.</summary>
public sealed class MemberMfaHttpTests : IAsyncLifetime
{
    private const string Password = "Correct-Password-123!";
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly PasetoTokenService tokens = new(Options.Create(new PasetoSetting
    {
        Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    }));
    private readonly Guid acme = Guid.NewGuid();
    private readonly Guid staffId = Guid.NewGuid();
    private WebApplication app = null!;
    private HttpClient client = null!;

    [Fact]
    public async Task A_required_member_enrolls_through_sign_in_and_then_answers_codes_with_documented_errors()
    {
        var login = new { organizationCode = "ACME123", email = "staff@acme.test", password = Password };

        using var needed = await Post("/api/v1/auth/login", login);
        using var wrongPassword = await Post("/api/v1/auth/mfa/enrollment",
            new { email = "staff@acme.test", password = "Wrong-Password-1!" });
        using var offer = await Post("/api/v1/auth/mfa/enrollment", new { email = "staff@acme.test", password = Password });
        var offered = await Result(offer);
        var secret = offered.GetProperty("secret").GetString()!;
        using var wrong = await Post("/api/v1/auth/login", new { login.organizationCode, login.email, login.password, totpCode = "000000" });
        using var enrolled = await Post("/api/v1/auth/login",
            new { login.organizationCode, login.email, login.password, totpCode = Code(secret) });
        var session = await Result(enrolled);
        using var asked = await Post("/api/v1/auth/login", login);
        using var replayed = await Post("/api/v1/auth/login",
            new { login.organizationCode, login.email, login.password, totpCode = Code(secret) });
        using var again = await Post("/api/v1/auth/mfa/enrollment", new { email = "staff@acme.test", password = Password });

        Assert.Equal((HttpStatusCode.Unauthorized, "MFA_ENROLLMENT_REQUIRED"), (needed.StatusCode, await ErrorCode(needed)));
        Assert.Equal((HttpStatusCode.Unauthorized, "INVALIDCREDENTIALS"), (wrongPassword.StatusCode, await ErrorCode(wrongPassword)));
        Assert.Equal(HttpStatusCode.OK, offer.StatusCode);
        Assert.StartsWith("otpauth://totp/", offered.GetProperty("provisioningUri").GetString());
        Assert.Equal((HttpStatusCode.Unauthorized, "MFAINVALID"), (wrong.StatusCode, await ErrorCode(wrong)));
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        Assert.Equal(10, session.GetProperty("recoveryCodes").GetArrayLength());
        Assert.StartsWith("v4.public.", session.GetProperty("accessToken").GetString());
        Assert.Equal((HttpStatusCode.Unauthorized, "MFAREQUIRED"), (asked.StatusCode, await ErrorCode(asked)));
        Assert.Equal((HttpStatusCode.Unauthorized, "MFAINVALID"), (replayed.StatusCode, await ErrorCode(replayed)));
        Assert.Equal((HttpStatusCode.Conflict, "MFA_ALREADY_ENROLLED"), (again.StatusCode, await ErrorCode(again)));
        Assert.Equal("no-store", enrolled.Headers.CacheControl?.ToString());

        // The signed-in endpoints, with the member's own token.
        var token = Token(staffId, acme);
        using var status = await Send(HttpMethod.Get, "/api/v1/mfa", token);
        using var anonymous = await Send(HttpMethod.Get, "/api/v1/mfa", null);
        using var admin = await Send(HttpMethod.Get, "/api/v1/mfa", Token(staffId, acme, "PlatformAdmin"));
        using var optIn = await Send(HttpMethod.Post, "/api/v1/mfa/enrollment", token);
        using var noCode = await Send(HttpMethod.Post, "/api/v1/mfa/recovery-codes", token, new { });
        using var regenerated = await Send(HttpMethod.Post, "/api/v1/mfa/recovery-codes", token,
            new { totpCode = Code(secret, 1) });
        using var disable = await Send(HttpMethod.Post, "/api/v1/mfa/disable", token, new { totpCode = "000000" });
        using var forged = await Send(HttpMethod.Get, "/api/v1/mfa", Token(staffId, acme, version: 9));

        var state = await Result(status);
        Assert.Equal((true, true, false, 10), (state.GetProperty("enabled").GetBoolean(),
            state.GetProperty("requiredByOrganization").GetBoolean(), state.GetProperty("canDisable").GetBoolean(),
            state.GetProperty("recoveryCodesRemaining").GetInt32()));
        Assert.Equal((HttpStatusCode.Unauthorized, "UNAUTHORIZED"), (anonymous.StatusCode, await ErrorCode(anonymous)));
        Assert.Equal(HttpStatusCode.Unauthorized, admin.StatusCode);
        Assert.Equal((HttpStatusCode.Conflict, "MFA_ALREADY_ENROLLED"), (optIn.StatusCode, await ErrorCode(optIn)));
        Assert.Equal((HttpStatusCode.Unauthorized, "MFAREQUIRED"), (noCode.StatusCode, await ErrorCode(noCode)));
        Assert.Equal(HttpStatusCode.OK, regenerated.StatusCode);
        Assert.Equal(10, (await Result(regenerated)).GetProperty("recoveryCodes").GetArrayLength());
        Assert.Equal((HttpStatusCode.Conflict, "MFA_REQUIRED_BY_ORGANIZATION"), (disable.StatusCode, await ErrorCode(disable)));
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
    }

    [Fact]
    public async Task Enrollment_is_refused_for_a_member_whose_organization_does_not_require_mfa()
    {
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Tenants.IgnoreQueryFilters().Single(x => x.Id == acme).RequireMfa = false;
            await db.SaveChangesAsync();
        }

        using var refused = await Post("/api/v1/auth/mfa/enrollment", new { email = "staff@acme.test", password = Password });
        using var login = await Post("/api/v1/auth/login",
            new { organizationCode = "ACME123", email = "staff@acme.test", password = Password });
        // A member may still opt in by choice, through the signed-in endpoints.
        using var offer = await Send(HttpMethod.Post, "/api/v1/mfa/enrollment", Token(staffId, acme));
        var secret = (await Result(offer)).GetProperty("secret").GetString()!;
        using var wrong = await Send(HttpMethod.Post, "/api/v1/mfa/enrollment/confirm", Token(staffId, acme), new { totpCode = "000000" });
        using var blank = await Send(HttpMethod.Post, "/api/v1/mfa/enrollment/confirm", Token(staffId, acme), new { });
        using var confirmed = await Send(HttpMethod.Post, "/api/v1/mfa/enrollment/confirm", Token(staffId, acme),
            new { totpCode = Code(secret) });
        var codes = (await Result(confirmed)).GetProperty("recoveryCodes");
        using var disabled = await Send(HttpMethod.Post, "/api/v1/mfa/disable", Token(staffId, acme),
            new { recoveryCode = codes[0].GetString() });

        Assert.Equal((HttpStatusCode.Conflict, "MFA_NOT_REQUIRED"), (refused.StatusCode, await ErrorCode(refused)));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, offer.StatusCode);
        Assert.Equal((HttpStatusCode.Unauthorized, "MFAINVALID"), (wrong.StatusCode, await ErrorCode(wrong)));
        Assert.Equal((HttpStatusCode.BadRequest, "INVALIDINPUT"), (blank.StatusCode, await ErrorCode(blank)));
        Assert.Equal(10, codes.GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
    }

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<TenantScope>();
        builder.Services.AddDbContext<IdentityDbContext>(options => options.UseSqlite(connection));
        builder.Services.AddSingleton<IPasetoTokenService>(tokens);
        builder.Services.AddSingleton<IEncryptionService>(new EncryptionService(Options.Create(new PasswordGenerateSetting())));
        builder.Services.AddSingleton<IMfaSecretProtector>(
            new AesGcmMfaSecretProtector(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
        builder.Services.AddSingleton<ISessionRevocationPublisher, NoRevocations>();
        builder.Services.AddSingleton<ICredentialNotificationSender, NoNotifications>();
        builder.Services.AddSingleton<ICredentialAttemptLimiter, NoLimit>();
        builder.Services.AddScoped<OnboardingService>();
        builder.Services.AddScoped<AuthenticationService>();
        builder.Services.AddControllers().AddApplicationPart(typeof(MemberMfaController).Assembly);
        app = builder.Build();
        app.MapControllers();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.EnsureCreatedAsync();
            var now = DateTime.UtcNow;
            db.Tenants.Add(new Tenant { Id = acme, Name = "Acme", Slug = "acme", OrganizationCode = "ACME123",
                RequireMfa = true, CreatedAtUtc = now });
            db.Accounts.Add(new Account { Id = staffId, Email = "staff@acme.test", CreatedAtUtc = now,
                PasswordHash = new EncryptionService(Options.Create(new PasswordGenerateSetting())).HashPassword(Password),
                PasswordChangedAtUtc = now.AddSeconds(1) });
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = acme, AccountId = staffId,
                Role = TenantRole.Employee, Status = MembershipStatus.Active, CreatedAtUtc = now });
            await db.SaveChangesAsync();
        }

        await app.StartAsync();
        client = app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
        await connection.DisposeAsync();
    }

    private static string Code(string secret, long stepOffset = 0) =>
        Totp.Compute(secret, Totp.StepAt(DateTimeOffset.UtcNow) + stepOffset);

    private string Token(Guid userId, Guid tenantId, string role = "Employee", long version = 0) =>
        tokens.GenerateToken(new PasetoTokenClaims
        {
            UserId = userId, TenantId = tenantId, TokenId = Guid.NewGuid().ToString(), Email = "caller@test",
            Role = role, SessionVersion = version
        }).Value;

    private Task<HttpResponseMessage> Post(string path, object body) => Send(HttpMethod.Post, path, null, body);

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Result(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("errorCode").GetString();
    }

    private sealed class NoRevocations : ISessionRevocationPublisher
    {
        public Task PublishAccountAsync(Guid tenantId, Guid accountId, long sessionVersion,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishTenantPolicyAsync(Guid tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteTenantPolicyAsync(Guid tenantId, long policyVersion,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoNotifications : ICredentialNotificationSender
    {
        public Task SendAsync(string email, string template, string code, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NoLimit : ICredentialAttemptLimiter
    {
        public Task CheckAsync(Guid tenantId, string normalizedEmail, string purpose,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
