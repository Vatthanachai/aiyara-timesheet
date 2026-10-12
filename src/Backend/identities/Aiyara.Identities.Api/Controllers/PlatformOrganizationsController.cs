using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Api.Controllers;

[Route("api/v1/platform/organizations")]
public sealed class PlatformOrganizationsController(PlatformActorResolver actors,
    PlatformOrganizationService organizations, PlatformConsoleService console,
    ICredentialNotificationSender notifications) : PlatformControllerBase(actors)
{
    [HttpGet]
    [ProducesResponseType(typeof(ServiceOkResponse<PlatformOrganizationListResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(string? search, int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (await PlatformActorAsync(cancellationToken) is null) return UnauthorizedResponse();
        if (search?.Length > 100)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_FAILED");
        var result = await organizations.ListAsync(search, Math.Max(page, 1),
            Math.Clamp(pageSize, 1, 100), cancellationToken);
        return ReturnResponseWithHttpStatus(new ServiceOkResponse<PlatformOrganizationListResponse>(result));
    }

    [HttpPost]
    [ProducesResponseType(typeof(ServiceOkResponse<PlatformOrganizationUpdateResult>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreatePlatformOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var actor = await PlatformActorAsync(cancellationToken);
        if (actor is null) return UnauthorizedResponse();
        try
        {
            var result = await organizations.CreateAsync(actor.AccountId, actor.SessionVersion,
                request, cancellationToken);
            return ReturnResponseWithHttpStatus(
                new ServiceOkResponse<PlatformOrganizationUpdateResult>(result, StatusCodes.Status201Created));
        }
        catch (PlatformOrganizationException exception) { return Error(exception); }
    }

    [HttpGet("{tenantId:guid}")]
    [ProducesResponseType(typeof(ServiceOkResponse<PlatformOrganizationDetail>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken cancellationToken)
    {
        if (await PlatformActorAsync(cancellationToken) is null) return UnauthorizedResponse();
        try
        {
            var result = await console.GetOrganizationAsync(tenantId, cancellationToken);
            return ReturnResponseWithHttpStatus(new ServiceOkResponse<PlatformOrganizationDetail>(result));
        }
        catch (PlatformOrganizationException exception) { return Error(exception); }
    }

    [HttpPut("{tenantId:guid}")]
    [ProducesResponseType(typeof(ServiceOkResponse<PlatformOrganizationUpdateResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid tenantId,
        [FromBody] UpdatePlatformOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await PlatformActorAsync(cancellationToken);
        if (actor is null) return UnauthorizedResponse();
        try
        {
            var result = await organizations.UpdateAsync(tenantId, actor.AccountId,
                actor.SessionVersion, request, cancellationToken);
            return ReturnResponseWithHttpStatus(new ServiceOkResponse<PlatformOrganizationUpdateResult>(result));
        }
        catch (PlatformOrganizationException exception) { return Error(exception); }
    }

    [HttpPut("{tenantId:guid}/status")]
    [ProducesResponseType(typeof(ServiceOkResponse<PlatformOrganizationUpdateResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SetStatus(Guid tenantId,
        [FromBody] SetPlatformOrganizationStatusRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await PlatformActorAsync(cancellationToken);
        if (actor is null) return UnauthorizedResponse();
        try
        {
            var result = await organizations.SetStatusAsync(tenantId, actor.AccountId,
                actor.SessionVersion, request, cancellationToken);
            return ReturnResponseWithHttpStatus(new ServiceOkResponse<PlatformOrganizationUpdateResult>(result));
        }
        catch (PlatformOrganizationException exception) { return Error(exception); }
        catch (AuthenticationException exception) when (exception.Failure == AuthenticationFailure.Unavailable)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "SESSION_REVOCATION_UNAVAILABLE");
        }
    }

    [HttpPost("{tenantId:guid}/administrators")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> InviteAdministrator(Guid tenantId,
        [FromBody] InvitePlatformOrganizationAdminRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var actor = await PlatformActorAsync(cancellationToken);
        if (actor is null) return UnauthorizedResponse();
        try
        {
            var issued = await organizations.InviteAdministratorAsync(tenantId,
                actor.AccountId, actor.SessionVersion, request, cancellationToken);
            await notifications.SendAsync(request.Email.Trim(), "invitation", issued.Code,
                cancellationToken);
            return ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(new
            {
                invitationId = issued.InvitationId,
                tenantId = issued.TenantId,
                expiresAtUtc = issued.ExpiresAtUtc,
                deliveryStatus = "accepted"
            }, StatusCodes.Status202Accepted));
        }
        catch (PlatformOrganizationException exception) { return Error(exception); }
        catch (AuthenticationException exception) when (exception.Failure == AuthenticationFailure.Unavailable)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "INVITATION_DELIVERY_UNAVAILABLE");
        }
    }

    [HttpPost("{tenantId:guid}/administrators/activation")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ResendAdministratorActivation(Guid tenantId,
        [FromBody] RequestPlatformAdministratorActivationRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await PlatformActorAsync(cancellationToken);
        if (actor is null) return UnauthorizedResponse();
        try
        {
            await organizations.ResendAdministratorActivationAsync(tenantId, actor.AccountId,
                actor.SessionVersion, request.Email, cancellationToken);
            return ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(new
            {
                deliveryStatus = "accepted"
            }));
        }
        catch (PlatformOrganizationException exception) { return Error(exception); }
        catch (AuthenticationException exception) when (exception.Failure == AuthenticationFailure.Unavailable)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "ACTIVATION_DELIVERY_UNAVAILABLE");
        }
    }

    [HttpPost("{tenantId:guid}/administrators/demote")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> DemoteAdministrator(Guid tenantId,
        [FromBody] ChangePlatformOrganizationAdminRequest request, CancellationToken cancellationToken) =>
        ChangeAdministrator(actor => organizations.DemoteAdministratorAsync(tenantId, actor.AccountId,
            actor.SessionVersion, request, cancellationToken), cancellationToken);

    [HttpPost("{tenantId:guid}/administrators/remove")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> RemoveAdministrator(Guid tenantId,
        [FromBody] ChangePlatformOrganizationAdminRequest request, CancellationToken cancellationToken) =>
        ChangeAdministrator(actor => organizations.RemoveAdministratorAsync(tenantId, actor.AccountId,
            actor.SessionVersion, request, cancellationToken), cancellationToken);

    private async Task<IActionResult> ChangeAdministrator(Func<PlatformActor, Task> change,
        CancellationToken cancellationToken)
    {
        var actor = await PlatformActorAsync(cancellationToken);
        if (actor is null) return UnauthorizedResponse();
        try
        {
            await change(actor);
            return ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(new { }));
        }
        catch (PlatformOrganizationException exception) { return Error(exception); }
        catch (AuthenticationException exception) when (exception.Failure == AuthenticationFailure.Unavailable)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "SESSION_REVOCATION_UNAVAILABLE");
        }
    }
}
