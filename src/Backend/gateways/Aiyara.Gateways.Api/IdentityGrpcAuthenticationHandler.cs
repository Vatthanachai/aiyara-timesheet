using System.Security.Claims;
using System.Text.Encodings.Web;
using Aiyara.Timesheet.Contracts.Identity.V1;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

internal sealed class IdentityGrpcAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IdentityValidationService.IdentityValidationServiceClient identity,
    RedisIdentityValidationCache cache)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authorization["Bearer ".Length..].Trim();
        if (token.Length == 0) return AuthenticateResult.Fail("Missing access token.");

        ValidateAccessTokenResponse validation;
        try
        {
            validation = await cache.GetAsync(token, Context.RequestAborted) ??
                await identity.ValidateAccessTokenAsync(new ValidateAccessTokenRequest
                {
                    AccessToken = token,
                    CorrelationId = Context.TraceIdentifier
                }, cancellationToken: Context.RequestAborted);
            if (validation.IsValid &&
                !await cache.SetAsync(token, validation, Context.RequestAborted))
                return AuthenticateResult.Fail("Access token was revoked.");
        }
        catch (RpcException)
        {
            return AuthenticateResult.Fail("Identity validation is unavailable.");
        }

        if (!validation.IsValid || !Guid.TryParse(validation.SubjectId, out _) ||
            !Guid.TryParse(validation.TenantId, out _))
        {
            return AuthenticateResult.Fail("Invalid access token.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, validation.SubjectId),
            new("tenant_id", validation.TenantId),
            new("token_id", validation.TokenId),
            new("session_version", validation.SessionVersion.ToString())
        };
        claims.AddRange(validation.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
