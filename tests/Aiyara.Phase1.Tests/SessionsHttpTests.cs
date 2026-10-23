using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
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

/// <summary>Session management over HTTP: a member lists and ends their own sessions only, the
/// current session is marked and protected, and a session ended on purpose does not sign the others out.</summary>
public sealed class SessionsHttpTests : IAsyncLifetime
{
    private const string Password = "Correct-Password-123!";
    private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
    private const string Phone = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly PasetoTokenService tokens = new(Options.Create(new PasetoSetting
    {
        Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    }));
    private readonly Guid acme = Guid.NewGuid();
    private readonly Guid globex = Guid.NewGuid();
    private readonly Guid staffId = Guid.NewGuid();
    private readonly Guid colleagueId = Guid.NewGuid();
    private WebApplication app = null!;
    private HttpClient client = null!;

    [Fact]
    public async Task A_member_lists_marks_and_ends_their_own_sessions()
    {
        var desktop = await Login("staff@acme.test", Chrome);
        var phone = await Login("staff@acme.test", Phone);
        var unknown = await Login("staff@acme.test", null);

        using var listed = await Send(HttpMethod.Get, "/api/v1/sessions", desktop.AccessToken);
        var rows = (await Result(listed)).EnumerateArray().ToList();
        using var anonymous = await Send(HttpMethod.Get, "/api/v1/sessions", null);

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Equal("no-store", listed.Headers.CacheControl?.ToString());
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "Chrome on Windows", "Safari on iOS", "Unknown device" }.Order(),
            rows.Select(x => x.GetProperty("device").GetString()!).Order());
        Assert.Equal(desktop.SessionId, rows.Single(x => x.GetProperty("current").GetBoolean()).GetProperty("id").GetGuid());
        Assert.All(rows, x => Assert.True(x.GetProperty("lastUsedAtUtc").GetDateTime() >= x.GetProperty("signedInAtUtc").GetDateTime()));
        Assert.Equal((HttpStatusCode.Unauthorized, "UNAUTHORIZED"), (anonymous.StatusCode, await ErrorCode(anonymous)));

        // Ending the phone: its refresh token stops working, the others keep working and keep their login.
        using var current = await Send(HttpMethod.Delete, $"/api/v1/sessions/{desktop.SessionId}", desktop.AccessToken);
        using var ended = await Send(HttpMethod.Delete, $"/api/v1/sessions/{phone.SessionId}", desktop.AccessToken);
        using var endedAgain = await Send(HttpMethod.Delete, $"/api/v1/sessions/{phone.SessionId}", desktop.AccessToken);
        using var refreshPhone = await Post("/api/v1/auth/refresh", new { refreshToken = phone.RefreshToken });
        using var refreshUnknown = await Post("/api/v1/auth/refresh", new { refreshToken = unknown.RefreshToken });
        using var refreshDesktop = await Post("/api/v1/auth/refresh", new { refreshToken = desktop.RefreshToken });

        Assert.Equal((HttpStatusCode.Conflict, "CURRENT_SESSION"), (current.StatusCode, await ErrorCode(current)));
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        Assert.Equal((HttpStatusCode.NotFound, "NOT_FOUND"), (endedAgain.StatusCode, await ErrorCode(endedAgain)));
        Assert.Equal(HttpStatusCode.Unauthorized, refreshPhone.StatusCode);
        Assert.Equal(HttpStatusCode.OK, refreshUnknown.StatusCode);
        Assert.Equal(HttpStatusCode.OK, refreshDesktop.StatusCode);

        // A rotated session keeps one row in the list, with the original sign-in time and device.
        var renewed = await Result(refreshDesktop);
        using var afterRefresh = await Send(HttpMethod.Get, "/api/v1/sessions", renewed.GetProperty("accessToken").GetString());
        var after = (await Result(afterRefresh)).EnumerateArray().ToList();
        var renewedRow = after.Single(x => x.GetProperty("current").GetBoolean());
        var originalRow = rows.Single(x => x.GetProperty("id").GetGuid() == desktop.SessionId);
        Assert.Equal(2, after.Count);
        Assert.Equal("Chrome on Windows", renewedRow.GetProperty("device").GetString());
        Assert.Equal(originalRow.GetProperty("signedInAtUtc").GetDateTime(), renewedRow.GetProperty("signedInAtUtc").GetDateTime());
    }

    [Fact]
    public async Task Another_accounts_session_is_not_found_and_stays_active()
    {
        var staff = await Login("staff@acme.test", Chrome);
        var colleague = await Login("colleague@acme.test", Phone);
        var outsider = await Login("outsider@globex.test", Chrome);

        using var colleagueSession = await Send(HttpMethod.Delete, $"/api/v1/sessions/{colleague.SessionId}", staff.AccessToken);
        using var outsiderSession = await Send(HttpMethod.Delete, $"/api/v1/sessions/{outsider.SessionId}", staff.AccessToken);
        using var unknownId = await Send(HttpMethod.Delete, $"/api/v1/sessions/{Guid.NewGuid()}", staff.AccessToken);
        using var staffList = await Send(HttpMethod.Get, "/api/v1/sessions", staff.AccessToken);
        using var outsiderList = await Send(HttpMethod.Get, "/api/v1/sessions", outsider.AccessToken);
        using var colleagueRefresh = await Post("/api/v1/auth/refresh", new { refreshToken = colleague.RefreshToken });
        using var outsiderRefresh = await Post("/api/v1/auth/refresh", new { refreshToken = outsider.RefreshToken });

        Assert.Equal((HttpStatusCode.NotFound, "NOT_FOUND"), (colleagueSession.StatusCode, await ErrorCode(colleagueSession)));
        Assert.Equal((HttpStatusCode.NotFound, "NOT_FOUND"), (outsiderSession.StatusCode, await ErrorCode(outsiderSession)));
        Assert.Equal(HttpStatusCode.NotFound, unknownId.StatusCode);
        Assert.Equal(new[] { staff.SessionId }, (await Result(staffList)).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));
        Assert.Equal(new[] { outsider.SessionId }, (await Result(outsiderList)).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.OK, colleagueRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, outsiderRefresh.StatusCode);
    }

    [Fact]
    public async Task Revoke_others_keeps_the_current_session_and_revoke_all_ends_everything()
    {
        var desktop = await Login("staff@acme.test", Chrome);
        var phone = await Login("staff@acme.test", Phone);
        var colleague = await Login("colleague@acme.test", Chrome);
        var legacyToken = Token(staffId, acme, sessionId: null);

        using var noCurrent = await Send(HttpMethod.Post, "/api/v1/sessions/revoke-others", legacyToken);
        using var others = await Send(HttpMethod.Post, "/api/v1/sessions/revoke-others", desktop.AccessToken);
        using var phoneRefresh = await Post("/api/v1/auth/refresh", new { refreshToken = phone.RefreshToken });
        using var colleagueRefresh = await Post("/api/v1/auth/refresh", new { refreshToken = colleague.RefreshToken });
        using var stillListed = await Send(HttpMethod.Get, "/api/v1/sessions", desktop.AccessToken);

        Assert.Equal((HttpStatusCode.Conflict, "SESSION_UNKNOWN"), (noCurrent.StatusCode, await ErrorCode(noCurrent)));
        Assert.Equal(HttpStatusCode.NoContent, others.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, phoneRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, colleagueRefresh.StatusCode);
        Assert.Equal(new[] { desktop.SessionId }, (await Result(stillListed)).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));

        using var all = await Send(HttpMethod.Post, "/api/v1/sessions/revoke-all", desktop.AccessToken);
        using var oldToken = await Send(HttpMethod.Get, "/api/v1/sessions", desktop.AccessToken);
        using var desktopRefresh = await Post("/api/v1/auth/refresh", new { refreshToken = desktop.RefreshToken });
        using var colleagueStill = await Post("/api/v1/auth/refresh", new { refreshToken = (await Result(colleagueRefresh)).GetProperty("refreshToken").GetString() });
        var signedBackIn = await Login("staff@acme.test", Chrome);
        using var fresh = await Send(HttpMethod.Get, "/api/v1/sessions", signedBackIn.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, all.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldToken.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, desktopRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, colleagueStill.StatusCode);
        Assert.Single((await Result(fresh)).EnumerateArray());
    }

    [Fact]
    public async Task A_revoked_session_cannot_sign_the_account_out_by_replaying_its_token_and_expired_ones_are_hidden()
    {
        var desktop = await Login("staff@acme.test", Chrome);
        var phone = await Login("staff@acme.test", Phone);
        await Send(HttpMethod.Delete, $"/api/v1/sessions/{phone.SessionId}", desktop.AccessToken);
        for (var attempt = 0; attempt < 3; attempt++)
            (await Post("/api/v1/auth/refresh", new { refreshToken = phone.RefreshToken })).Dispose();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var old = await Login("staff@acme.test", Phone);
            (await db.RefreshSessions.IgnoreQueryFilters().SingleAsync(x => x.Id == old.SessionId))
                .ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        using var listed = await Send(HttpMethod.Get, "/api/v1/sessions", desktop.AccessToken);
        using var refreshDesktop = await Post("/api/v1/auth/refresh", new { refreshToken = desktop.RefreshToken });

        Assert.Equal(new[] { desktop.SessionId }, (await Result(listed)).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.OK, refreshDesktop.StatusCode);
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36 Edg/130.0", "Edge on Windows")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Gecko/20100101 Firefox/131.0", "Firefox on macOS")]
    [InlineData("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/130.0.0.0 Mobile Safari/537.36", "Chrome on Android")]
    [InlineData("curl/8.0", "Unknown device")]
    [InlineData("", "Unknown device")]
    public void Device_labels_name_the_browser_and_system(string userAgent, string expected) =>
        Assert.Equal(expected, SessionDeviceName.From(userAgent));

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<TenantScope>();
        builder.Services.AddScoped<SessionDeviceContext>();
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
        builder.Services.AddControllers().AddApplicationPart(typeof(SessionsController).Assembly);
        app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.RequestServices.GetRequiredService<SessionDeviceContext>().UserAgent =
                context.Request.Headers.UserAgent.ToString();
            await next(context);
        });
        app.MapControllers();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.EnsureCreatedAsync();
            var now = DateTime.UtcNow;
            var hash = new EncryptionService(Options.Create(new PasswordGenerateSetting())).HashPassword(Password);
            db.Tenants.Add(new Tenant { Id = acme, Name = "Acme", Slug = "acme", OrganizationCode = "ACME123", CreatedAtUtc = now });
            db.Tenants.Add(new Tenant { Id = globex, Name = "Globex", Slug = "globex", OrganizationCode = "GLOBX45", CreatedAtUtc = now });
            foreach (var (id, email, tenant) in new[]
                {
                    (staffId, "staff@acme.test", acme), (colleagueId, "colleague@acme.test", acme),
                    (Guid.NewGuid(), "outsider@globex.test", globex)
                })
            {
                db.Accounts.Add(new Account { Id = id, Email = email, CreatedAtUtc = now, PasswordHash = hash,
                    PasswordChangedAtUtc = now.AddSeconds(1) });
                db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenant, AccountId = id,
                    Role = TenantRole.Employee, Status = MembershipStatus.Active, CreatedAtUtc = now });
            }
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

    private string Token(Guid userId, Guid tenantId, Guid? sessionId) =>
        tokens.GenerateToken(new PasetoTokenClaims
        {
            UserId = userId, TenantId = tenantId, TokenId = Guid.NewGuid().ToString(), Email = "caller@test",
            Role = "Employee", SessionVersion = 0, SessionId = sessionId
        }).Value;

    private sealed record Signed(string AccessToken, string RefreshToken, Guid SessionId);

    private async Task<Signed> Login(string email, string? userAgent)
    {
        var code = email.EndsWith("globex.test") ? "GLOBX45" : "ACME123";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { organizationCode = code, email, password = Password })
        };
        if (userAgent is not null) request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Result(response);
        var access = result.GetProperty("accessToken").GetString()!;
        return new Signed(access, result.GetProperty("refreshToken").GetString()!,
            tokens.ValidateToken(access)!.SessionId!.Value);
    }

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
