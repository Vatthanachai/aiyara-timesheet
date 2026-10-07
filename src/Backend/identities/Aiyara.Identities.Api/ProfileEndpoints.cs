using System.Buffers;
using System.Text;
using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Api;

public static class ProfileEndpoints
{
    private const int MaxPhotoBytes = 5 * 1024 * 1024;
    private const string PhotoPath = "/api/v1/identity/profile/me/photo";

    public static void MapProfileEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/tenants/{tenantId:guid}/password-policy", async (Guid tenantId,
            HttpContext context, IdentityDbContext db, IPasetoTokenService tokens,
            TenantScope scope) =>
        {
            var account = await AuthenticatedAccount(context, db, tokens, scope);
            if (account is null || scope.TenantId != tenantId) return Results.Unauthorized();
            var membership = await db.Memberships.SingleAsync(x => x.AccountId == account.Id);
            if (membership.Role != TenantRole.TenantAdmin)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var tenant = await db.Tenants.SingleAsync();
            return Results.Ok(new { minimumLength = tenant.PasswordMinimumLength,
                expiryDays = tenant.PasswordExpiryDays, requireUppercase = tenant.PasswordRequireUppercase,
                requireLowercase = tenant.PasswordRequireLowercase, requireDigit = tenant.PasswordRequireDigit,
                requireSymbol = tenant.PasswordRequireSymbol });
        }).WithName("GetTenantPasswordPolicyV1").WithTags("Tenant administration");

        var group = app.MapGroup("/api/v1/profile").WithTags("Profile");
        group.MapGet("/me", async (HttpContext context, IdentityDbContext db,
            IPasetoTokenService tokens, TenantScope scope) =>
        {
            var account = await AuthenticatedAccount(context, db, tokens, scope);
            if (account is null) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(View(account));
        }).WithName("GetMyProfileV1");

        group.MapPut("/me", async (HttpContext context, IdentityDbContext db,
            IPasetoTokenService tokens, TenantScope scope, ProfileRequest body) =>
        {
            var account = await AuthenticatedAccount(context, db, tokens, scope);
            if (account is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(body.FirstName) || body.FirstName.Length > 100 ||
                string.IsNullOrWhiteSpace(body.LastName) || body.LastName.Length > 100 ||
                body.JobTitle?.Length > 160)
                return Results.BadRequest("Invalid profile fields.");
            account.FirstName = body.FirstName.Trim();
            account.LastName = body.LastName.Trim();
            account.JobTitle = body.JobTitle?.Trim();
            await db.SaveChangesAsync();
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(View(account));
        }).WithName("UpdateMyProfileV1");

        group.MapPost("/me/photo", async (HttpContext context, IdentityDbContext db,
            IPasetoTokenService tokens, TenantScope scope, IProfilePhotoStorage storage,
            IFormFile photo, CancellationToken cancellationToken) =>
        {
            var account = await AuthenticatedAccount(context, db, tokens, scope);
            if (account is null) return Results.Unauthorized();
            if (photo.Length is < 1 or > MaxPhotoBytes)
                return Results.BadRequest("Choose an image no larger than 5 MB.");

            await using var input = photo.OpenReadStream();
            await using var content = new MemoryStream();
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    if (content.Length + read > MaxPhotoBytes)
                        return Results.BadRequest("Choose an image no larger than 5 MB.");
                    await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            var bytes = content.ToArray();
            var contentType = DetectImageType(bytes);
            if (contentType is null)
                return Results.BadRequest("Upload a valid JPEG, PNG, or WebP image.");

            var tenantId = scope.TenantId ?? throw new InvalidOperationException("Authenticated tenant scope is missing.");
            account.PhotoUrl = await storage.PutAsync(tenantId, account.Id, bytes,
                contentType, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { photoUrl = PhotoPath });
        }).WithName("UploadMyProfilePhotoV1")
            .WithMetadata(new RequestSizeLimitAttribute(MaxPhotoBytes + 64 * 1024))
            .DisableAntiforgery();

        group.MapGet("/me/photo", async (HttpContext context, IdentityDbContext db,
            IPasetoTokenService tokens, TenantScope scope, IProfilePhotoStorage storage,
            CancellationToken cancellationToken) =>
        {
            var account = await AuthenticatedAccount(context, db, tokens, scope);
            if (account is null) return Results.Unauthorized();
            if (account.PhotoUrl is null || Uri.TryCreate(account.PhotoUrl, UriKind.Absolute, out _))
                return Results.NotFound();

            try
            {
                var tenantId = scope.TenantId ?? throw new InvalidOperationException("Authenticated tenant scope is missing.");
                var photo = await storage.GetAsync(tenantId, account.Id, cancellationToken);
                if (photo is null) return Results.NotFound();
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                return Results.File(photo.Bytes, photo.ContentType);
            }
            catch (Amazon.S3.AmazonS3Exception exception) when
                (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return Results.NotFound();
            }
        }).WithName("GetMyProfilePhotoV1");

        group.MapDelete("/me/photo", async (HttpContext context, IdentityDbContext db,
            IPasetoTokenService tokens, TenantScope scope, IProfilePhotoStorage storage,
            CancellationToken cancellationToken) =>
        {
            var account = await AuthenticatedAccount(context, db, tokens, scope);
            if (account is null) return Results.Unauthorized();
            if (account.PhotoUrl is not null && !Uri.TryCreate(account.PhotoUrl, UriKind.Absolute, out _))
            {
                var tenantId = scope.TenantId ?? throw new InvalidOperationException("Authenticated tenant scope is missing.");
                await storage.DeleteAsync(tenantId, account.Id, cancellationToken);
            }
            account.PhotoUrl = null;
            await db.SaveChangesAsync(cancellationToken);
            context.Response.Headers.CacheControl = "no-store";
            return Results.NoContent();
        }).WithName("DeleteMyProfilePhotoV1");
    }

    private static async Task<Account?> AuthenticatedAccount(HttpContext context, IdentityDbContext db,
        IPasetoTokenService tokens, TenantScope scope)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        var claims = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? tokens.ValidateToken(authorization[7..].Trim()) : null;
        if (claims is null || claims.MustChangePassword) return null;
        scope.TenantId = claims.TenantId;
        var membership = await db.Memberships.SingleOrDefaultAsync(x => x.AccountId == claims.UserId &&
            x.Status == MembershipStatus.Active);
        if (membership is null || membership.Role.ToString() != claims.Role) return null;
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == claims.UserId);
        return account is not null && account.SessionVersion == claims.SessionVersion &&
            !account.MustChangePassword && !membership.MustChangePassword ? account : null;
    }

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

public sealed record ProfileRequest(string FirstName, string LastName, string? JobTitle);
