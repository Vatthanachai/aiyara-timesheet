using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Aiyara.Timesheet.Databases;
using Microsoft.AspNetCore.Mvc;

namespace Aiyara.Timesheet.Api.Controllers;

[Route("api/v1")]
public sealed class TimesheetController(TimesheetOperations operations) : BaseController
{
    [HttpGet("context", Name = "GetTimesheetContextV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> Context(CancellationToken ct) => Execute(actor => operations.Context(actor, ct));

    [HttpGet("catalog", Name = "GetTimesheetCatalogV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> Catalog(CancellationToken ct) => Execute(actor => operations.Catalog(actor, ct));

    [HttpPost("projects")]
    [ProducesResponseType(typeof(ServiceOkResponse<Project>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> CreateProject([FromBody] ProjectRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveCatalog(actor, body, "project", ct));

    [HttpPost("categories")]
    [ProducesResponseType(typeof(ServiceOkResponse<Category>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> CreateCategory([FromBody] NameRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveCatalog(actor, body, "category", ct));

    [HttpPut("projects/{id:guid}")]
    [ProducesResponseType(typeof(ServiceOkResponse<Project>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> UpdateProject(Guid id, [FromBody] NameRequest body, CancellationToken ct) =>
        Execute(actor => operations.UpdateCatalog(actor, id, body, "project", ct));

    [HttpPut("categories/{id:guid}")]
    [ProducesResponseType(typeof(ServiceOkResponse<Category>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> UpdateCategory(Guid id, [FromBody] NameRequest body, CancellationToken ct) =>
        Execute(actor => operations.UpdateCatalog(actor, id, body, "category", ct));

    /// <summary>Tenant Admin only: replaces a project's client, billable flag, hourly rate and currency, and hours budget.</summary>
    [HttpPut("projects/{id:guid}/billing", Name = "UpdateProjectBillingV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<Project>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> UpdateProjectBilling(Guid id, [FromBody] ProjectBillingRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveProjectBilling(actor, id, body, ct));

    /// <summary>Tenant Admin only: every project's hours used against its budget and its billable amount for
    /// the period (inclusive <c>from</c> and <c>to</c> dates; all time when both are left out).</summary>
    [HttpGet("projects/usage", Name = "GetProjectsUsageV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ProjectsUsage([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Execute(actor => operations.ProjectsUsage(actor, from, to, ct));

    /// <summary>Tenant Admin only: one project's usage, with the minutes each member logged on it in the period.</summary>
    [HttpGet("projects/{id:guid}/usage", Name = "GetProjectUsageV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> ProjectUsage(Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        CancellationToken ct) => Execute(actor => operations.ProjectUsageOf(actor, id, from, to, ct));

    /// <summary>Tenant Admin only: usage grouped by client, with the billable amount per currency.</summary>
    [HttpGet("clients/usage", Name = "GetClientsUsageV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ClientsUsage([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Execute(actor => operations.ClientsUsage(actor, from, to, ct));

    [HttpDelete("projects/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> DeleteProject(Guid id, CancellationToken ct) =>
        Execute(actor => operations.DeleteCatalog(actor, id, "project", ct));

    [HttpDelete("categories/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct) =>
        Execute(actor => operations.DeleteCatalog(actor, id, "category", ct));

    [HttpPost("holidays", Name = "CreateHolidayV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<Holiday>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> CreateHoliday([FromBody] HolidayRequest body, CancellationToken ct) =>
        Execute(actor => operations.CreateHoliday(actor, body, ct));

    [HttpPut("holidays/{id:guid}", Name = "UpdateHolidayV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<Holiday>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> UpdateHoliday(Guid id, [FromBody] HolidayRequest body,
        CancellationToken ct) => Execute(actor => operations.UpdateHoliday(actor, id, body, ct));

    [HttpDelete("holidays/{id:guid}", Name = "DeleteHolidayV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> DeleteHoliday(Guid id, CancellationToken ct) =>
        Execute(actor => operations.DeleteHoliday(actor, id, ct));

    /// <summary>Tenant Admin only: sets the organization's working weekdays (Sunday = 0).</summary>
    [HttpPut("work-week", Name = "UpdateWorkWeekV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> UpdateWorkWeek([FromBody] WorkWeekRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveWorkWeek(actor, body, ct));

    /// <summary>Tenant Admin only: whether members are emailed about time they have not logged, and when.</summary>
    [HttpGet("reminders", Name = "GetReminderSettingsV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Reminders(CancellationToken ct) => Execute(actor => operations.Reminders(actor, ct));

    [HttpPut("reminders", Name = "UpdateReminderSettingsV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> UpdateReminders([FromBody] ReminderSettingsRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveReminders(actor, body, ct));

    /// <summary>Tenant Admin only: adds a leave type with its yearly days (null for no limit).</summary>
    [HttpPost("leave-types", Name = "CreateLeaveTypeV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<LeaveType>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> CreateLeaveType([FromBody] LeaveTypeRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveLeaveType(actor, null, body, ct));

    [HttpPut("leave-types/{id:guid}", Name = "UpdateLeaveTypeV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<LeaveType>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> UpdateLeaveType(Guid id, [FromBody] LeaveTypeRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveLeaveType(actor, id, body, ct));

    [HttpDelete("leave-types/{id:guid}", Name = "DeleteLeaveTypeV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> DeleteLeaveType(Guid id, CancellationToken ct) =>
        Execute(actor => operations.DeleteLeaveType(actor, id, ct));

    /// <summary>The signed-in member's leave taken and left per leave type in a calendar year.</summary>
    [HttpGet("me/leave-balance", Name = "GetMyLeaveBalanceV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> MyLeaveBalance([FromQuery] int year, CancellationToken ct) =>
        Execute(actor => operations.MyLeaveBalance(actor, year, ct));

    /// <summary>Tenant Admin only: a member's leave taken and left per leave type in a calendar year.</summary>
    [HttpGet("members/{ownerId:guid}/leave-balance", Name = "GetMemberLeaveBalanceV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> MemberLeaveBalance(Guid ownerId, [FromQuery] int year, CancellationToken ct) =>
        Execute(actor => operations.MemberLeaveBalance(actor, ownerId, year, ct));

    [HttpGet("tasks", Name = "GetPersonalTasksV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<IReadOnlyList<PersonalTask>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> Tasks(CancellationToken ct) => Execute(actor => operations.Tasks(actor, ct));

    [HttpPost("tasks", Name = "CreatePersonalTaskV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<PersonalTask>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> CreateTask([FromBody] TaskRequest body, CancellationToken ct) =>
        Execute(actor => operations.CreateTask(actor, body, ct));

    [HttpPut("tasks/{id:guid}", Name = "UpdatePersonalTaskV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<PersonalTask>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> UpdateTask(Guid id, [FromBody] TaskRequest body, CancellationToken ct) =>
        Execute(actor => operations.UpdateTask(actor, id, body, ct));

    [HttpDelete("tasks/{id:guid}", Name = "DeletePersonalTaskV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> DeleteTask(Guid id, CancellationToken ct) =>
        Execute(actor => operations.DeleteTask(actor, id, ct));

    [HttpGet("entries", Name = "GetTimeEntriesV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<IReadOnlyList<TimeEntry>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> Entries([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Execute(actor => operations.Entries(actor, year, month, ct));

    /// <summary>Creates an entry. With <c>id</c> the create is idempotent: a repeat answers <c>200</c> with the
    /// entry already written; an id that is not the member's is <c>409 ID_IN_USE</c>.</summary>
    [HttpPost("entries", Name = "CreateTimeEntryV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<TimeEntry>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceOkResponse<TimeEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> CreateEntry([FromBody] EntryRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveEntry(actor, null, body, ct));

    [HttpPut("entries/{id:guid}", Name = "UpdateTimeEntryV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<TimeEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> UpdateEntry(Guid id, [FromBody] EntryRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveEntry(actor, id, body, ct));

    [HttpDelete("entries/{id:guid}", Name = "DeleteTimeEntryV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> DeleteEntry(Guid id, [FromQuery] string? reason, CancellationToken ct) =>
        Execute(actor => operations.DeleteEntry(actor, id, reason, ct));

    /// <summary>Writes the same entry on each chosen date, all or none; closed days are named in <c>errors.dates</c>.</summary>
    [HttpPost("entries/bulk", Name = "CreateBulkTimeEntriesV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> BulkEntries([FromBody] BulkEntryRequest body, CancellationToken ct) =>
        Execute(actor => operations.BulkEntries(actor, body, ct));

    /// <summary>Copies a day or a week of the member's entries onto other days, skipping days off,
    /// holidays, whole-day leave, and days that already have entries.</summary>
    [HttpPost("entries/copy", Name = "CopyTimeEntriesV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> CopyEntries([FromBody] CopyEntriesRequest body, CancellationToken ct) =>
        Execute(actor => operations.CopyEntries(actor, body, ct));

    /// <summary>The signed-in member's own entry templates, by name.</summary>
    [HttpGet("entry-templates", Name = "GetEntryTemplatesV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<IReadOnlyList<EntryTemplate>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> EntryTemplates(CancellationToken ct) =>
        Execute(actor => operations.EntryTemplates(actor, ct));

    [HttpPost("entry-templates", Name = "CreateEntryTemplateV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<EntryTemplate>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> CreateEntryTemplate([FromBody] EntryTemplateRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveEntryTemplate(actor, null, body, ct));

    [HttpPut("entry-templates/{id:guid}", Name = "UpdateEntryTemplateV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<EntryTemplate>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> UpdateEntryTemplate(Guid id, [FromBody] EntryTemplateRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveEntryTemplate(actor, id, body, ct));

    [HttpDelete("entry-templates/{id:guid}", Name = "DeleteEntryTemplateV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> DeleteEntryTemplate(Guid id, CancellationToken ct) =>
        Execute(actor => operations.DeleteEntryTemplate(actor, id, ct));

    /// <summary>The member's running timer (<c>timer</c> is null when none) and the server's time, <c>nowUtc</c>.</summary>
    [HttpGet("timer", Name = "GetTimerV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> Timer(CancellationToken ct) => Execute(actor => operations.Timer(actor, ct));

    /// <summary>Starts the member's timer now; <c>409 TIMER_RUNNING</c> when one already runs.</summary>
    [HttpPost("timer/start", Name = "StartTimerV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> StartTimer([FromBody] StartTimerRequest? body, CancellationToken ct) =>
        Execute(actor => operations.StartTimer(actor, body, ct));

    /// <summary>Stops the timer into a time entry with the break left out. A refusal keeps the timer:
    /// <c>409 MONTH_CLOSED</c>, <c>409 TIMER_TOO_LONG</c>, or an entry error (400).</summary>
    [HttpPost("timer/stop", Name = "StopTimerV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<TimeEntry>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> StopTimer([FromBody] StopTimerRequest? body, CancellationToken ct) =>
        Execute(actor => operations.StopTimer(actor, body, ct));

    [HttpDelete("timer", Name = "DiscardTimerV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> DiscardTimer(CancellationToken ct) => Execute(actor => operations.DiscardTimer(actor, ct));

    [HttpGet("leave", Name = "GetLeaveEntriesV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<IReadOnlyList<LeaveEntry>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> Leaves([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Execute(actor => operations.Leaves(actor, year, month, ct));

    [HttpPost("leave", Name = "CreateLeaveEntryV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<LeaveEntry>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> CreateLeave([FromBody] LeaveRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveLeave(actor, null, body, ct));

    [HttpPut("leave/{id:guid}", Name = "UpdateLeaveEntryV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<LeaveEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> UpdateLeave(Guid id, [FromBody] LeaveRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveLeave(actor, id, body, ct));

    [HttpDelete("leave/{id:guid}", Name = "DeleteLeaveEntryV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> DeleteLeave(Guid id, CancellationToken ct) =>
        Execute(actor => operations.DeleteLeave(actor, id, ct));

    [HttpPost("months/{year:int}/{month:int}/lock", Name = "LockTimesheetMonthV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<MonthLock>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> LockMonth(int year, int month, CancellationToken ct) =>
        Execute(actor => operations.LockMonth(actor, year, month, ct));

    [HttpGet("months/{year:int}/{month:int}/snapshot", Name = "GetTimesheetMonthSnapshotV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Snapshot(int year, int month, [FromQuery] Guid? ownerId, CancellationToken ct) =>
        Execute(actor => operations.Snapshot(actor, year, month, ownerId, ct));

    /// <summary>The signed-in member's month: approval status, return reason, lock, and whether it can be edited.</summary>
    [HttpGet("me/months/{year:int}/{month:int}", Name = "GetMyTimesheetMonthV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> MyMonth(int year, int month, CancellationToken ct) =>
        Execute(actor => operations.MyMonth(actor, year, month, ct));

    /// <summary>Submits the signed-in member's open or returned month for approval.</summary>
    [HttpPost("me/months/{year:int}/{month:int}/submit", Name = "SubmitTimesheetMonthV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<MonthApproval>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> SubmitMonth(int year, int month, CancellationToken ct) =>
        Execute(actor => operations.SubmitMonth(actor, year, month, ct));

    /// <summary>Tenant Admin only: approves a member's submitted month and takes its snapshot.</summary>
    [HttpPost("members/{ownerId:guid}/months/{year:int}/{month:int}/approve", Name = "ApproveTimesheetMonthV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<MonthApproval>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> ApproveMonth(Guid ownerId, int year, int month, CancellationToken ct) =>
        Execute(actor => operations.ApproveMonth(actor, ownerId, year, month, ct));

    /// <summary>Tenant Admin only: returns a member's submitted month with a reason and emails them.</summary>
    [HttpPost("members/{ownerId:guid}/months/{year:int}/{month:int}/return", Name = "ReturnTimesheetMonthV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<MonthApproval>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> ReturnMonth(Guid ownerId, int year, int month, [FromBody] ReturnRequest body,
        CancellationToken ct) => Execute(actor => operations.ReturnMonth(actor, ownerId, year, month, body, ct));

    /// <summary>Tenant Admin only: hours, entry count, and leave days per member for a month.</summary>
    [HttpGet("members/summary", Name = "GetMemberTimesheetSummaryV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> MemberSummary([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Execute(actor => operations.MemberSummary(actor, year, month, ct));

    /// <summary>Tenant Admin only: one member's entries, leave, and the month's holidays.</summary>
    [HttpGet("members/{ownerId:guid}/timesheet", Name = "GetMemberTimesheetV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> MemberTimesheet(Guid ownerId, [FromQuery] int year, [FromQuery] int month,
        CancellationToken ct) => Execute(actor => operations.MemberTimesheet(actor, ownerId, year, month, ct));

    /// <summary>The signed-in member's own entries, leave, holidays, and working days for a month.</summary>
    [HttpGet("me/timesheet", Name = "GetMyTimesheetV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> MyTimesheet([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Execute(actor => operations.MyTimesheet(actor, year, month, ct));

    private async Task<IActionResult> Execute(Func<Actor, Task<ServiceResponse>> action)
    {
        if (!Actor.TryRead(HttpContext, out var actor)) return ReturnResponseWithHttpStatus(
            new ServiceErrorResponse(401, "UNAUTHORIZED", "Authentication is required."));
        return ReturnResponseWithHttpStatus(await action(actor));
    }
}
