using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Aiyara.Timesheet.Databases;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Timesheet.Api.Controllers;

[Route("internal/v1/report-snapshots")]
public sealed class InternalSnapshotsController(TimesheetDbContext db,
    TimesheetTenantScope scope, IConfiguration configuration) : BaseController
{
    [HttpGet("{subjectId:guid}/{year:int}/{month:int}", Name = "GetInternalReportSnapshotV1")]
    public async Task<IActionResult> Get(Guid subjectId, int year, int month, CancellationToken ct)
    {
        var failure = Validate(year, month);
        if (failure is not null) return ReturnResponseWithHttpStatus(failure);
        var snapshot = await db.MonthSnapshots.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OwnerId == subjectId && x.Year == year && x.Month == month, ct);
        return ReturnResponseWithHttpStatus(snapshot is null
            ? new ServiceErrorResponse(404, "NOT_FOUND", "Snapshot was not found.")
            : new ServiceOkResponse<JsonElement>(JsonSerializer.Deserialize<JsonElement>(snapshot.PayloadJson)));
    }

    [HttpGet("{year:int}/{month:int}", Name = "ListInternalReportSnapshotsV1")]
    public async Task<IActionResult> List(int year, int month, CancellationToken ct)
    {
        var failure = Validate(year, month);
        if (failure is not null) return ReturnResponseWithHttpStatus(failure);
        var snapshots = await db.MonthSnapshots.AsNoTracking().Where(x =>
            x.Year == year && x.Month == month && x.OwnerId != Guid.Empty)
            .Select(x => new { x.OwnerId, x.PayloadJson, x.CreatedAtUtc }).ToListAsync(ct);
        return ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(snapshots));
    }

    private ServiceErrorResponse? Validate(int year, int month)
    {
        var configuredKey = configuration["Reporting:InternalKey"];
        var suppliedKey = Request.Headers["X-Internal-Service-Key"].ToString();
        if (string.IsNullOrEmpty(configuredKey) || string.IsNullOrEmpty(suppliedKey) ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey)),
                SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey))))
            return new ServiceErrorResponse(401, "UNAUTHORIZED", "Authentication is required.");
        if (!Guid.TryParse(Request.Headers["X-Tenant-Id"], out var tenantId) ||
            tenantId == Guid.Empty || year is < 2000 or > 2100 || month is < 1 or > 12)
            return new ServiceErrorResponse(400, "VALIDATION_FAILED", "The request contains invalid fields.");
        scope.TenantId = tenantId;
        return null;
    }
}
