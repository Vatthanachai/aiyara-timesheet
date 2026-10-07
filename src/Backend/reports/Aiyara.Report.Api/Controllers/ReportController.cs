using Aiyara.Report.Models;
using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Microsoft.AspNetCore.Mvc;

namespace Aiyara.Report.Api.Controllers;

[Route("api/v1")]
public sealed class ReportController(ReportOperations operations) : BaseController
{
    [HttpGet("context")]
    public IActionResult Context() => WithActor(actor =>
        new ServiceOkResponse<object>(new { actor.TenantId, actor.UserId,
            actor.TimeZoneId, actor.IsAdmin }));

    [HttpGet("definitions")]
    public Task<IActionResult> Definitions(CancellationToken ct) =>
        WithActorAsync(actor => operations.Definitions(actor, ct));

    [HttpGet("dashboard")]
    public Task<IActionResult> Dashboard(CancellationToken ct) =>
        WithActorAsync(actor => operations.Dashboard(actor, ct));

    [HttpPost("definitions")]
    public Task<IActionResult> CreateDefinition([FromBody] DefinitionRequest body, CancellationToken ct) =>
        WithActorAsync(actor => operations.CreateDefinition(actor, body, ct), adminOnly: true);

    [HttpPut("definitions/{id:guid}")]
    public Task<IActionResult> UpdateDefinition(Guid id, [FromBody] DefinitionRequest body,
        CancellationToken ct) => WithActorAsync(actor => operations.UpdateDefinition(actor, id, body, ct), adminOnly: true);

    [HttpDelete("definitions/{id:guid}")]
    public Task<IActionResult> DeleteDefinition(Guid id, CancellationToken ct) =>
        WithActorAsync(actor => operations.DeleteDefinition(actor, id, ct), adminOnly: true);

    [HttpGet("runs")]
    public Task<IActionResult> Runs([FromQuery] int? take, CancellationToken ct) =>
        WithActorAsync(actor => operations.Runs(actor, take, ct));

    [HttpPost("runs")]
    public Task<IActionResult> CreateRun([FromBody] RunRequest body, CancellationToken ct) =>
        WithActorAsync(actor => operations.CreateRun(actor, body, ct));

    [HttpGet("runs/{id:guid}/download")]
    public Task<IActionResult> Download(Guid id, CancellationToken ct) =>
        WithActorAsync(actor => operations.Download(actor, id, ct));

    [HttpPost("runs/{id:guid}/signed-document")]
    [IgnoreAntiforgeryToken]
    public Task<IActionResult> UploadSignedDocument(Guid id, IFormFile file, CancellationToken ct) =>
        WithActorAsync(actor => operations.UploadSignedDocument(actor, id, file, ct), adminOnly: true);

    [HttpGet("schedules")]
    public Task<IActionResult> Schedules(CancellationToken ct) =>
        WithActorAsync(actor => operations.Schedules(actor, ct), adminOnly: true);

    [HttpPost("schedules")]
    public Task<IActionResult> CreateSchedule([FromBody] ScheduleRequest body, CancellationToken ct) =>
        WithActorAsync(actor => operations.CreateSchedule(actor, body, ct), adminOnly: true);

    [HttpDelete("schedules/{id:guid}")]
    public Task<IActionResult> DeleteSchedule(Guid id, CancellationToken ct) =>
        WithActorAsync(actor => operations.DeleteSchedule(actor, id, ct), adminOnly: true);

    [HttpGet("retention")]
    public Task<IActionResult> Retention(CancellationToken ct) =>
        WithActorAsync(actor => operations.Retention(actor, ct), adminOnly: true);

    [HttpPut("retention")]
    public Task<IActionResult> UpdateRetention([FromBody] RetentionRequest body, CancellationToken ct) =>
        WithActorAsync(actor => operations.UpdateRetention(actor, body, ct), adminOnly: true);

    private IActionResult WithActor(Func<ReportActor, ServiceResponse> action, bool adminOnly = false)
    {
        if (!ReportActor.TryRead(HttpContext, out var actor)) return Error(401, "UNAUTHORIZED");
        if (adminOnly && !actor.IsAdmin) return Error(403, "FORBIDDEN");
        return ReturnResponseWithHttpStatus(action(actor));
    }

    private async Task<IActionResult> WithActorAsync(Func<ReportActor, Task<ServiceResponse>> action,
        bool adminOnly = false)
    {
        if (!ReportActor.TryRead(HttpContext, out var actor)) return Error(401, "UNAUTHORIZED");
        if (adminOnly && !actor.IsAdmin) return Error(403, "FORBIDDEN");
        return ReturnResponseWithHttpStatus(await action(actor));
    }

    private IActionResult Error(int status, string code) => ReturnResponseWithHttpStatus(
        new ServiceErrorResponse(status, code, "The request could not be completed."));
}
