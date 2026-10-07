using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

[Route("api/v1")]
[EnableRateLimiting("onboarding")]
public sealed class OnboardingController(IdentityOnboardingClient client) : BaseController
{
    [HttpPost("tenants", Name = "GatewayCreateTenantV1")]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return ReturnResponseWithHttpStatus(await client.CreateTenantAsync(request, ct));
    }

    [HttpPost("tenants/{tenantId:guid}/invitations", Name = "GatewayIssueInvitationV1")]
    public async Task<IActionResult> IssueInvitation(Guid tenantId,
        [FromBody] IssueInvitationRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return ReturnResponseWithHttpStatus(await client.IssueInvitationAsync(tenantId, request,
            Request.Headers["X-Onboarding-Key"].ToString(), ct));
    }

    [HttpPost("invitations/accept", Name = "GatewayAcceptInvitationV1")]
    public async Task<IActionResult> AcceptInvitation([FromBody] AcceptInvitationRequest request,
        CancellationToken ct) => ReturnResponseWithHttpStatus(await client.AcceptInvitationAsync(request, ct));
}
