using System.Buffers;
using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Amazon.S3;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Api.Controllers;

[Route("api/v1")]
public sealed class ProfileController(IdentityDbContext db, IPasetoTokenService tokens,
    TenantScope scope) : BaseController
{
    private const int MaxPhotoBytes = 5 * 1024 * 1024;
    private const string PhotoPath = "/api/v1/identity/profile/me/photo";
    [HttpGet("tenants/{tenantId:guid}/password-policy", Name = "GetTenantPasswordPolicyV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> PasswordPolicy(Guid tenantId, CancellationToken ct)
    {
        var account = await AuthenticatedAccount(ct);
        if (account is null || scope.TenantId != tenantId) return Error(401, "UNAUTHORIZED");
        var membership = await db.Memberships.SingleAsync(x => x.AccountId == account.Id, ct);
        if (membership.Role != TenantRole.TenantAdmin) return Error(403, "FORBIDDEN");
        var tenant = await db.Tenants.SingleAsync(ct);
        return ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(new
        {
            minimumLength = tenant.PasswordMinimumLength,
            expiryDays = tenant.PasswordExpiryDays,
            requireUppercase = tenant.PasswordRequireUppercase,
            requireLowercase = tenant.PasswordRequireLowercase,
            requireDigit = tenant.PasswordRequireDigit,
            requireSymbol = tenant.PasswordRequireSymbol
        }));
    }

    [HttpGet("profile/me", Name = "GetMyProfileV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var account = await AuthenticatedAccount(ct);
        return account is null ? Error(401, "UNAUTHORIZED") :
            ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(View(account)));
    }

    [HttpPut("profile/me", Name = "UpdateMyProfileV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateMe([FromBody] ProfileRequest body, CancellationToken ct)
    {
        var account = await AuthenticatedAccount(ct);
        if (account is null) return Error(401, "UNAUTHORIZED");
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(body.FirstName) || body.FirstName.Length > 100)
            errors["firstName"] = ["INVALID_VALUE"];
        if (string.IsNullOrWhiteSpace(body.LastName) || body.LastName.Length > 100)
            errors["lastName"] = ["INVALID_VALUE"];
        if (body.JobTitle?.Length > 160) errors["jobTitle"] = ["INVALID_VALUE"];
        if (body.PhotoUrl?.Length > 2048 || body.PhotoUrl is not null &&
            (!Uri.TryCreate(body.PhotoUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            errors["photoUrl"] = ["INVALID_VALUE"];
        if (errors.Count > 0) return ReturnResponseWithHttpStatus(
            new ServiceErrorResponse(400, "VALIDATION_FAILED", "The request contains invalid fields.", errors));
        account.FirstName = body.FirstName.Trim();
        account.LastName = body.LastName.Trim();
        account.JobTitle = body.JobTitle?.Trim();
        account.PhotoUrl = body.PhotoUrl;
        await db.SaveChangesAsync(ct);
        return ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(View(account)));
    }

    [HttpPost("profile/me/photo", Name = "UploadMyProfilePhotoV1")]
    [RequestSizeLimit(MaxPhotoBytes + 64 * 1024)]
    [IgnoreAntiforgeryToken]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UploadPhoto([FromForm] IFormFile photo,
        [FromServices] IProfilePhotoStorage storage, CancellationToken ct)
    {
        var account = await AuthenticatedAccount(ct);
        if (account is null) return Error(401, "UNAUTHORIZED");
        if (photo.Length is < 1 or > MaxPhotoBytes)
            return Error(400, "INVALID_PHOTO");

        await using var input = photo.OpenReadStream();
        await using var content = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                if (content.Length + read > MaxPhotoBytes)
                    return Error(400, "INVALID_PHOTO");
                await content.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }

        var bytes = content.ToArray();
        var contentType = DetectImageType(bytes);
        if (contentType is null) return Error(400, "INVALID_PHOTO");
        var tenantId = scope.TenantId ?? throw new InvalidOperationException("Authenticated tenant scope is missing.");
        account.PhotoUrl = await storage.PutAsync(tenantId, account.Id, bytes, contentType, ct);
        await db.SaveChangesAsync(ct);
        Response.Headers.CacheControl = "no-store";
        return ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(new { photoUrl = PhotoPath }));
    }

    [HttpGet("profile/me/photo", Name = "GetMyProfilePhotoV1")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhoto([FromServices] IProfilePhotoStorage storage, CancellationToken ct)
    {
        var account = await AuthenticatedAccount(ct);
        if (account is null) return Error(401, "UNAUTHORIZED");
        if (account.PhotoUrl is null || Uri.TryCreate(account.PhotoUrl, UriKind.Absolute, out _))
            return Error(404, "NOT_FOUND");
        try
        {
            var tenantId = scope.TenantId ?? throw new InvalidOperationException("Authenticated tenant scope is missing.");
            var photo = await storage.GetAsync(tenantId, account.Id, ct);
            if (photo is null) return Error(404, "NOT_FOUND");
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers.XContentTypeOptions = "nosniff";
            return ReturnResponseWithHttpStatus(new ServiceFileResponse(photo.Bytes, photo.ContentType, "profile-photo"));
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Error(404, "NOT_FOUND");
        }
    }

    [HttpDelete("profile/me/photo", Name = "DeleteMyProfilePhotoV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeletePhoto([FromServices] IProfilePhotoStorage storage, CancellationToken ct)
    {
        var account = await AuthenticatedAccount(ct);
        if (account is null) return Error(401, "UNAUTHORIZED");
        if (account.PhotoUrl is not null && !Uri.TryCreate(account.PhotoUrl, UriKind.Absolute, out _))
        {
            var tenantId = scope.TenantId ?? throw new InvalidOperationException("Authenticated tenant scope is missing.");
            await storage.DeleteAsync(tenantId, account.Id, ct);
        }
        account.PhotoUrl = null;
        await db.SaveChangesAsync(ct);
        Response.Headers.CacheControl = "no-store";
        return ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(new { }, 204));
    }

    private async Task<Account?> AuthenticatedAccount(CancellationToken ct)
    {
        var authorization = Request.Headers.Authorization.ToString();
        var claims = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? tokens.ValidateToken(authorization[7..].Trim()) : null;
        if (claims is null || claims.MustChangePassword) return null;
        scope.TenantId = claims.TenantId;
        var membership = await db.Memberships.SingleOrDefaultAsync(x => x.AccountId == claims.UserId &&
            x.Status == MembershipStatus.Active, ct);
        if (membership is null || membership.Role.ToString() != claims.Role) return null;
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == claims.UserId, ct);
        return account is not null && account.SessionVersion == claims.SessionVersion &&
            !account.MustChangePassword && !membership.MustChangePassword ? account : null;
    }

    private IActionResult Error(int status, string code) => ReturnResponseWithHttpStatus(
        new ServiceErrorResponse(status, code, "The request could not be completed."));

    private static object View(Account account) => new
    {
        account.Id, account.Email, account.FirstName, account.LastName,
        PhotoUrl = account.PhotoUrl is null ? null :
            Uri.TryCreate(account.PhotoUrl, UriKind.Absolute, out _) ? account.PhotoUrl : PhotoPath,
        account.JobTitle
    };

    private static string? DetectImageType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff)
            return "image/jpeg";
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[]
            { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
            return "image/png";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) &&
            bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }
}

public sealed record ProfileRequest(string FirstName, string LastName, string? PhotoUrl, string? JobTitle);
