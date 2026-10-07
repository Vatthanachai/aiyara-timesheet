using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Microsoft.AspNetCore.Mvc;

namespace Aiyara.Timesheet.Api.Controllers;

[Route("api/v1")]
public sealed class TimesheetController(TimesheetOperations operations) : BaseController
{
    [HttpGet("context", Name = "GetTimesheetContextV1")]
    public Task<IActionResult> Context(CancellationToken ct) => Execute(actor => operations.Context(actor, ct));

    [HttpGet("catalog", Name = "GetTimesheetCatalogV1")]
    public Task<IActionResult> Catalog(CancellationToken ct) => Execute(actor => operations.Catalog(actor, ct));

    [HttpPost("projects")]
    public Task<IActionResult> CreateProject([FromBody] NameRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveCatalog(actor, body, "project", ct));

    [HttpPost("categories")]
    public Task<IActionResult> CreateCategory([FromBody] NameRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveCatalog(actor, body, "category", ct));

    [HttpPut("projects/{id:guid}")]
    public Task<IActionResult> UpdateProject(Guid id, [FromBody] NameRequest body, CancellationToken ct) =>
        Execute(actor => operations.UpdateCatalog(actor, id, body, "project", ct));

    [HttpPut("categories/{id:guid}")]
    public Task<IActionResult> UpdateCategory(Guid id, [FromBody] NameRequest body, CancellationToken ct) =>
        Execute(actor => operations.UpdateCatalog(actor, id, body, "category", ct));

    [HttpDelete("projects/{id:guid}")]
    public Task<IActionResult> DeleteProject(Guid id, CancellationToken ct) =>
        Execute(actor => operations.DeleteCatalog(actor, id, "project", ct));

    [HttpDelete("categories/{id:guid}")]
    public Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct) =>
        Execute(actor => operations.DeleteCatalog(actor, id, "category", ct));

    [HttpPost("holidays", Name = "CreateHolidayV1")]
    public Task<IActionResult> CreateHoliday([FromBody] HolidayRequest body, CancellationToken ct) =>
        Execute(actor => operations.CreateHoliday(actor, body, ct));

    [HttpPut("holidays/{id:guid}", Name = "UpdateHolidayV1")]
    public Task<IActionResult> UpdateHoliday(Guid id, [FromBody] HolidayRequest body,
        CancellationToken ct) => Execute(actor => operations.UpdateHoliday(actor, id, body, ct));

    [HttpDelete("holidays/{id:guid}", Name = "DeleteHolidayV1")]
    public Task<IActionResult> DeleteHoliday(Guid id, CancellationToken ct) =>
        Execute(actor => operations.DeleteHoliday(actor, id, ct));

    [HttpGet("tasks", Name = "GetPersonalTasksV1")]
    public Task<IActionResult> Tasks(CancellationToken ct) => Execute(actor => operations.Tasks(actor, ct));

    [HttpPost("tasks", Name = "CreatePersonalTaskV1")]
    public Task<IActionResult> CreateTask([FromBody] TaskRequest body, CancellationToken ct) =>
        Execute(actor => operations.CreateTask(actor, body, ct));

    [HttpPut("tasks/{id:guid}", Name = "UpdatePersonalTaskV1")]
    public Task<IActionResult> UpdateTask(Guid id, [FromBody] TaskRequest body, CancellationToken ct) =>
        Execute(actor => operations.UpdateTask(actor, id, body, ct));

    [HttpDelete("tasks/{id:guid}", Name = "DeletePersonalTaskV1")]
    public Task<IActionResult> DeleteTask(Guid id, CancellationToken ct) =>
        Execute(actor => operations.DeleteTask(actor, id, ct));

    [HttpGet("entries", Name = "GetTimeEntriesV1")]
    public Task<IActionResult> Entries([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Execute(actor => operations.Entries(actor, year, month, ct));

    [HttpPost("entries", Name = "CreateTimeEntryV1")]
    public Task<IActionResult> CreateEntry([FromBody] EntryRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveEntry(actor, null, body, ct));

    [HttpPut("entries/{id:guid}", Name = "UpdateTimeEntryV1")]
    public Task<IActionResult> UpdateEntry(Guid id, [FromBody] EntryRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveEntry(actor, id, body, ct));

    [HttpDelete("entries/{id:guid}", Name = "DeleteTimeEntryV1")]
    public Task<IActionResult> DeleteEntry(Guid id, [FromQuery] string? reason, CancellationToken ct) =>
        Execute(actor => operations.DeleteEntry(actor, id, reason, ct));

    [HttpGet("leave", Name = "GetLeaveEntriesV1")]
    public Task<IActionResult> Leaves([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Execute(actor => operations.Leaves(actor, year, month, ct));

    [HttpPost("leave", Name = "CreateLeaveEntryV1")]
    public Task<IActionResult> CreateLeave([FromBody] LeaveRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveLeave(actor, null, body, ct));

    [HttpPut("leave/{id:guid}", Name = "UpdateLeaveEntryV1")]
    public Task<IActionResult> UpdateLeave(Guid id, [FromBody] LeaveRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveLeave(actor, id, body, ct));

    [HttpDelete("leave/{id:guid}", Name = "DeleteLeaveEntryV1")]
    public Task<IActionResult> DeleteLeave(Guid id, CancellationToken ct) =>
        Execute(actor => operations.DeleteLeave(actor, id, ct));

    [HttpPost("months/{year:int}/{month:int}/lock", Name = "LockTimesheetMonthV1")]
    public Task<IActionResult> LockMonth(int year, int month, CancellationToken ct) =>
        Execute(actor => operations.LockMonth(actor, year, month, ct));

    [HttpGet("months/{year:int}/{month:int}/snapshot", Name = "GetTimesheetMonthSnapshotV1")]
    public Task<IActionResult> Snapshot(int year, int month, [FromQuery] Guid? ownerId, CancellationToken ct) =>
        Execute(actor => operations.Snapshot(actor, year, month, ownerId, ct));

    private async Task<IActionResult> Execute(Func<Actor, Task<ServiceResponse>> action)
    {
        if (!Actor.TryRead(HttpContext, out var actor)) return ReturnResponseWithHttpStatus(
            new ServiceErrorResponse(401, "UNAUTHORIZED", "Authentication is required."));
        return ReturnResponseWithHttpStatus(await action(actor));
    }
}
