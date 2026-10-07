using System.Text.Json;
using System.Data;
using Aiyara.Timesheet.Databases;
using Aiyara.Timesheet.Contracts.Messaging.V1;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Timesheet.Api;

public sealed record NameRequest(string Name);
public sealed record HolidayRequest(DateOnly Date, string Name);
public sealed record TaskRequest(string Name, string Status, Guid? ProjectId, Guid? CategoryId);
public sealed record EntryRequest(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    string TaskName, string? Detail, string? Notes, Guid? ProjectId, Guid? CategoryId,
    Guid? PersonalTaskId, string? Reason);
public sealed record LeaveRequest(DateOnly Date, string Kind, string? Notes);

public sealed record Actor(Guid TenantId, Guid UserId, string Role, string TimeZoneId)
{
    public const string ContextKey = "TimesheetValidatedActor";
    public bool IsAdmin => Role is "TenantAdmin" or "PlatformAdmin";

    public static bool TryRead(HttpContext ctx, out Actor actor)
    {
        actor = ctx.Items[ContextKey] as Actor ?? default!;
        return actor is not null;
    }
}
