using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aiyara.Timesheet.Component.Abstractions.Securities;

/// <summary>
/// Authenticates requests by validating the PASETO token from the 'Authorization: Bearer' header
/// and mapping its claims onto the request's ClaimsPrincipal.
/// </summary>
public class PasetoAuthenticationHandler(
    IOptionsMonitor<PasetoAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IPasetoTokenService pasetoTokenService)
    : AuthenticationHandler<PasetoAuthenticationOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authorizationHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var headerValue = authorizationHeader.ToString();
        if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = headerValue["Bearer ".Length..].Trim();
        var claims = pasetoTokenService.ValidateToken(token);
        if (claims is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired token."));
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, claims.UserId.ToString()),
                new Claim(ClaimTypes.Email, claims.Email ?? string.Empty),
                new Claim("security_stamp", claims.SecurityStamp ?? string.Empty),
                new Claim("must_change_password", claims.MustChangePassword.ToString()),
                new Claim(ClaimTypes.Role, claims.Role ?? string.Empty),
            ],
            Scheme.Name);

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
