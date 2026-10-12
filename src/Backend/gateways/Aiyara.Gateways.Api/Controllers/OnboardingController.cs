using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

// Gateway forwards these requests verbatim to Identity and relays its status code and
// envelope, so the documented shapes mirror Identity's onboarding contract rather than
// a Gateway-specific one. See IdentityOnboardingClient.ForwardAsync.
[Route("api/v1")]
[EnableRateLimiting("onboarding")]
public sealed class OnboardingController(IdentityOnboardingClient client) : BaseController
{
    [HttpPost("tenants", Name = "GatewayCreateTenantV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<CreateTenantResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return ReturnResponseWithHttpStatus(await client.CreateTenantAsync(request, ct));
    }

    [HttpPost("tenants/{tenantId:guid}/invitations", Name = "GatewayIssueInvitationV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<IssueInvitationResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> IssueInvitation(Guid tenantId,
        [FromBody] IssueInvitationRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return ReturnResponseWithHttpStatus(await client.IssueInvitationAsync(tenantId, request,
            Request.Headers["X-Onboarding-Key"].ToString(), ct));
    }

    [HttpPost("invitations/accept", Name = "GatewayAcceptInvitationV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<AcceptInvitationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> AcceptInvitation([FromBody] AcceptInvitationRequest request,
        CancellationToken ct) => ReturnResponseWithHttpStatus(await client.AcceptInvitationAsync(request, ct));
}
