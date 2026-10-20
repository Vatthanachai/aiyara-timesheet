using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Amazon.S3;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Api.Controllers;

public sealed record OrganizationNameRequest(string Name);

/// <summary>The caller's organization as the Shell header shows it: every member reads the name
/// and logo; only a Tenant Admin changes them. Gateway exposes these under
/// <c>/api/v1/identity/organization</c>.</summary>
[Route("api/v1/organization")]
public sealed class OrganizationController(IdentityDbContext db, AuthenticatedMemberResolver members,
    OrganizationBrandingService branding, IOrganizationLogoStorage storage) : BaseController
{
    private const int MaxLogoBytes = 2 * 1024 * 1024;
    private const string LogoPath = "/api/v1/identity/organization/logo";

    [HttpGet(Name = "GetMyOrganizationV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (await members.ResolveAsync(Request, ct) is null) return Error(401, "UNAUTHORIZED");
        return ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(View(await db.Tenants.SingleAsync(ct))));
    }

    [HttpPut(Name = "RenameMyOrganizationV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Rename([FromBody] OrganizationNameRequest body, CancellationToken ct) =>
        AsAdmin(async admin =>
        {
            await branding.RenameAsync(admin.TenantId, admin.Account.Id, admin.SessionVersion, body.Name, ct);
            return Current(View(await db.Tenants.SingleAsync(ct)));
        }, ct);

    /// <summary>Replaces the logo with a JPEG, PNG, or WebP image of at most 2 MB.</summary>
    [HttpPost("logo", Name = "UploadMyOrganizationLogoV1")]
    [RequestSizeLimit(MaxLogoBytes + 64 * 1024)]
    [IgnoreAntiforgeryToken]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> UploadLogo([FromForm] IFormFile logo, CancellationToken ct) =>
        AsAdmin(async admin =>
        {
            await branding.EnsureAdminAsync(admin.TenantId, admin.Account.Id, admin.SessionVersion, ct);
            if (await ImageUploads.ReadAsync(logo, MaxLogoBytes, ct) is not var (bytes, contentType))
                return Error(400, "INVALID_LOGO");
            var key = await storage.PutLogoAsync(admin.TenantId, bytes, contentType, ct);
            await branding.SetLogoAsync(admin.TenantId, admin.Account.Id, key, ct);
            return Current(View(await db.Tenants.SingleAsync(ct)));
        }, ct);

    [HttpGet("logo", Name = "GetMyOrganizationLogoV1")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLogo(CancellationToken ct)
    {
        if (await members.ResolveAsync(Request, ct) is null) return Error(401, "UNAUTHORIZED");
        var key = (await db.Tenants.SingleAsync(ct)).LogoKey;
        if (key is null) return Error(404, "NOT_FOUND");
        try
        {
            var logo = await storage.GetLogoAsync(key, ct);
            if (logo is null) return Error(404, "NOT_FOUND");
            // The URL carries the logo's version, so a changed logo is a new URL.
            Response.Headers.CacheControl = "private, max-age=86400";
            Response.Headers.XContentTypeOptions = "nosniff";
            return ReturnResponseWithHttpStatus(new ServiceFileResponse(logo.Bytes, logo.ContentType, "organization-logo"));
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Error(404, "NOT_FOUND");
        }
    }

    [HttpDelete("logo", Name = "DeleteMyOrganizationLogoV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> DeleteLogo(CancellationToken ct) =>
        AsAdmin(async admin =>
        {
            if (await branding.RemoveLogoAsync(admin.TenantId, admin.Account.Id, admin.SessionVersion, ct) is { } key)
                await storage.DeleteLogoAsync(key, ct);
            return Current(View(await db.Tenants.SingleAsync(ct)));
        }, ct);

    /// <summary>Runs a change for a Tenant Admin; the service re-checks session version and
    /// password age in the database.</summary>
    private async Task<IActionResult> AsAdmin(Func<AuthenticatedMember, Task<IActionResult>> action,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var member = await members.ResolveAsync(Request, ct);
        if (member is null) return Error(401, "UNAUTHORIZED");
        if (member.Role != TenantRole.TenantAdmin) return Error(403, "FORBIDDEN");
        try { return await action(member); }
        catch (TenantMemberException exception)
        {
            return exception.Failure == TenantMemberFailure.InvalidInput
                ? Error(400, "VALIDATION_FAILED") : Error(401, "UNAUTHORIZED");
        }
    }

    private static object View(Tenant tenant) => new
    {
        tenant.Name, tenant.OrganizationCode,
        LogoUrl = tenant.LogoKey is null ? null : $"{LogoPath}?v={tenant.LogoUpdatedAtUtc?.Ticks ?? 0}"
    };

    private IActionResult Current(object value) => ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(value));

    private IActionResult Error(int status, string code) => ReturnResponseWithHttpStatus(
        new ServiceErrorResponse(status, code, "The request could not be completed."));
}
