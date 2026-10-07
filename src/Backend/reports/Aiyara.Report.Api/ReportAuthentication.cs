using Aiyara.Report.Databases;
using Aiyara.Timesheet.Contracts.Identity.V1;
using Grpc.Core;

namespace Aiyara.Report.Api;

public sealed record ReportTokenValidation(bool IsValid, string TenantId, string SubjectId,
    string TimeZoneId, IReadOnlyList<string> Roles);

public interface IReportAccessTokenValidator
{
    Task<ReportTokenValidation> ValidateAsync(string accessToken, string correlationId,
        CancellationToken cancellationToken);
}

public sealed class GrpcReportAccessTokenValidator(
    IdentityValidationService.IdentityValidationServiceClient identity) : IReportAccessTokenValidator
{
    public async Task<ReportTokenValidation> ValidateAsync(string accessToken, string correlationId,
        CancellationToken cancellationToken)
    {
        var response = await identity.ValidateAccessTokenAsync(new ValidateAccessTokenRequest
        {
            AccessToken = accessToken,
            CorrelationId = correlationId
        }, cancellationToken: cancellationToken);
        return new ReportTokenValidation(response.IsValid, response.TenantId, response.SubjectId,
            response.TimeZoneId, response.Roles.ToArray());
    }
}

public static class ReportAuthentication
{
    public static IApplicationBuilder UseReportAuthentication(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api/v1"))
            {
                await next();
                return;
            }

            var authorization = context.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            ReportTokenValidation validation;
            try
            {
                validation = await context.RequestServices.GetRequiredService<IReportAccessTokenValidator>()
                    .ValidateAsync(authorization[7..].Trim(), context.TraceIdentifier, context.RequestAborted);
            }
            catch (RpcException)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            if (!validation.IsValid || !Guid.TryParse(validation.TenantId, out var tenantId) ||
                !Guid.TryParse(validation.SubjectId, out var userId))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            context.RequestServices.GetRequiredService<ReportingTenantScope>().TenantId = tenantId;
            context.Items[ReportActor.ContextKey] = new ReportActor(tenantId, userId,
                validation.Roles.FirstOrDefault(role => role is "TenantAdmin" or "PlatformAdmin") ??
                validation.Roles.FirstOrDefault() ?? "", string.IsNullOrWhiteSpace(validation.TimeZoneId)
                    ? "Asia/Bangkok" : validation.TimeZoneId);
            await next();
        });
}
