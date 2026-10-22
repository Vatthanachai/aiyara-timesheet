using System.Text.Json;
using System.Text.RegularExpressions;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Aiyara.Timesheet.Databases;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Timesheet.Api;

/// <summary>Billable work and budgets: a project's billing details, and hours used against budget and the
/// billable amount by project and by client. See the Billable Work And Budgets Plan.</summary>
public sealed partial class TimesheetOperations
{
    private const decimal MaxMoney = 1_000_000m;

    /// <summary>The billing details after trimming and normalizing, as they are stored.</summary>
    private sealed record Billing(string? Client, decimal? HourlyRate, string? CurrencyCode, decimal? BudgetHours);

    /// <summary>Tenant Admin only: replaces a project's client, billable flag, rate, currency, and hours budget.
    /// Time already logged is not changed; reports use the details as they are when they are read.</summary>
    public async Task<ServiceResponse> SaveProjectBilling(Actor actor, Guid id, ProjectBillingRequest body,
        CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (project is null) return Error(404, "NOT_FOUND");
        var billing = CheckBilling(body.Client, body.HourlyRate, body.CurrencyCode, body.BudgetHours);
        if (billing is null) return Error(400, "VALIDATION_FAILED");
        var before = JsonSerializer.Serialize(project);
        project.IsBillable = body.IsBillable;
        ApplyBilling(project, billing);
        Audit(actor, "project", id, "billing_updated", before, project);
        await db.SaveChangesAsync(ct);
        return Ok(project);
    }

    private static void ApplyBilling(Project project, Billing billing)
    {
        project.Client = billing.Client; project.HourlyRate = billing.HourlyRate;
        project.CurrencyCode = billing.CurrencyCode; project.BudgetHours = billing.BudgetHours;
    }

    /// <summary>Null when something is out of range: a client over 200 characters, a rate below 0, above
    /// 1,000,000, or with more than two decimals, a currency that is not three letters or is missing with a
    /// rate, or a budget that is not above 0 up to 1,000,000 in hundredths.</summary>
    private static Billing? CheckBilling(string? client, decimal? rate, string? currency, decimal? budget)
    {
        client = string.IsNullOrWhiteSpace(client) ? null : client.Trim();
        currency = string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant();
        if (client?.Length > 200 ||
            rate is { } hourly && (hourly < 0 || hourly > MaxMoney || hourly != decimal.Round(hourly, 2)) ||
            budget is { } hours && (hours <= 0 || hours > MaxMoney || hours != decimal.Round(hours, 2)) ||
            currency is not null && !CurrencyPattern().IsMatch(currency) ||
            rate is not null && currency is null)
            return null;
        return new Billing(client, rate, currency, budget);
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyPattern();

    /// <summary>One project's usage: the hours logged in the period and in total against its budget, and the
    /// billable amount for the period.</summary>
    /// <param name="PeriodMinutes">Minutes in the period (all time when no dates were given).</param>
    /// <param name="BillableMinutes">The period's minutes when the project is billable, otherwise 0.</param>
    /// <param name="BillableAmount">Billable hours times today's rate, to two decimals; null without a rate or
    /// when the project is not billable.</param>
    /// <param name="UsedMinutes">Minutes logged on the project in all time, which the budget counts.</param>
    /// <param name="RemainingHours">Budget less used hours, negative when over; null without a budget.</param>
    public sealed record ProjectUsage(Guid ProjectId, string Name, string? Client, bool IsBillable, decimal? HourlyRate,
        string? CurrencyCode, decimal? BudgetHours, bool IsDeleted, int PeriodMinutes, int BillableMinutes,
        decimal? BillableAmount, int UsedMinutes, decimal UsedHours, decimal? RemainingHours, bool OverBudget);

    /// <summary>Money in one currency; amounts in different currencies are never added together.</summary>
    public sealed record CurrencyAmount(string CurrencyCode, decimal Amount);

    public sealed record UsageTotals(int PeriodMinutes, int BillableMinutes, CurrencyAmount[] Amounts);

    /// <summary>One client's projects together; <c>Client</c> is null for projects without one.</summary>
    public sealed record ClientUsage(string? Client, int ProjectCount, int PeriodMinutes, int BillableMinutes,
        int NonBillableMinutes, CurrencyAmount[] Amounts);

    /// <summary>Tenant Admin only: usage of every project, with a total. <paramref name="from"/> and
    /// <paramref name="to"/> are inclusive dates in the organization's calendar; leave both out for all time.</summary>
    public async Task<ServiceResponse> ProjectsUsage(Actor actor, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        if (!ValidPeriod(from, to)) return Error(400, "INVALID_DATE");
        var (rows, unassigned) = await UsageRowsAsync(from, to, null, ct);
        return Ok(new { from, to, unassignedMinutes = unassigned, projects = rows, totals = Totals(rows) });
    }

    /// <summary>Tenant Admin only: one project's usage and the minutes each member logged on it in the period.</summary>
    public async Task<ServiceResponse> ProjectUsageOf(Actor actor, Guid id, DateOnly? from, DateOnly? to,
        CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        if (!ValidPeriod(from, to)) return Error(400, "INVALID_DATE");
        if (!await db.Projects.AnyAsync(x => x.Id == id && !x.IsDeleted, ct)) return Error(404, "NOT_FOUND");
        var (rows, _) = await UsageRowsAsync(from, to, id, ct);
        var members = (await InPeriod(db.TimeEntries.AsNoTracking().Where(x => !x.IsDeleted && x.ProjectId == id),
                from, to).GroupBy(x => x.OwnerId)
            .Select(x => new { OwnerId = x.Key, Minutes = x.Sum(e => e.DurationMinutes) }).ToListAsync(ct))
            .OrderByDescending(x => x.Minutes).ThenBy(x => x.OwnerId);
        return Ok(new { from, to, project = rows.Single(), members });
    }

    /// <summary>Tenant Admin only: the projects grouped by client (ignoring case), with the hours and the
    /// billable amount per currency. Projects without a client are one group with a null client.</summary>
    public async Task<ServiceResponse> ClientsUsage(Actor actor, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        if (!ValidPeriod(from, to)) return Error(400, "INVALID_DATE");
        var (rows, unassigned) = await UsageRowsAsync(from, to, null, ct);
        var clients = rows.GroupBy(x => x.Client?.ToUpperInvariant())
            .Select(group => new ClientUsage(group.First().Client,
                group.Count(), group.Sum(x => x.PeriodMinutes), group.Sum(x => x.BillableMinutes),
                group.Sum(x => x.PeriodMinutes - x.BillableMinutes), Amounts(group)))
            .OrderBy(x => x.Client is null).ThenBy(x => x.Client, StringComparer.OrdinalIgnoreCase).ToArray();
        return Ok(new { from, to, unassignedMinutes = unassigned, clients, totals = Totals(rows) });
    }

    private static bool ValidPeriod(DateOnly? from, DateOnly? to) =>
        (from is null || ValidMonth(from.Value.Year, 1)) && (to is null || ValidMonth(to.Value.Year, 1)) &&
        (from is null || to is null || from <= to);

    private static IQueryable<TimeEntry> InPeriod(IQueryable<TimeEntry> entries, DateOnly? from, DateOnly? to) =>
        entries.Where(x => (from == null || x.Date >= from) && (to == null || x.Date <= to));

    /// <summary>A project's row for each live project (and each deleted one with time in the period), or just
    /// <paramref name="only"/>, with the period's minutes on no project.</summary>
    private async Task<(ProjectUsage[] Rows, int Unassigned)> UsageRowsAsync(DateOnly? from, DateOnly? to, Guid? only,
        CancellationToken ct)
    {
        var live = db.TimeEntries.AsNoTracking().Where(x => !x.IsDeleted);
        var total = await live.Where(x => x.ProjectId != null && (only == null || x.ProjectId == only))
            .GroupBy(x => x.ProjectId!.Value).Select(x => new { Id = x.Key, Minutes = x.Sum(e => e.DurationMinutes) })
            .ToDictionaryAsync(x => x.Id, x => x.Minutes, ct);
        var period = await InPeriod(live.Where(x => x.ProjectId != null && (only == null || x.ProjectId == only)), from, to)
            .GroupBy(x => x.ProjectId!.Value).Select(x => new { Id = x.Key, Minutes = x.Sum(e => e.DurationMinutes) })
            .ToDictionaryAsync(x => x.Id, x => x.Minutes, ct);
        var unassigned = only is not null ? 0 : await InPeriod(live.Where(x => x.ProjectId == null), from, to)
            .SumAsync(x => (int?)x.DurationMinutes, ct) ?? 0;
        var projects = await db.Projects.AsNoTracking().Where(x => only == null || x.Id == only).ToListAsync(ct);
        var rows = projects.Where(x => !x.IsDeleted || period.ContainsKey(x.Id))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id)
            .Select(project => Usage(project, period.GetValueOrDefault(project.Id), total.GetValueOrDefault(project.Id)))
            .ToArray();
        return (rows, unassigned);
    }

    private static ProjectUsage Usage(Project project, int periodMinutes, int usedMinutes)
    {
        var billable = project.IsBillable ? periodMinutes : 0;
        decimal? amount = project.IsBillable && project.HourlyRate is { } rate
            ? Math.Round(billable / 60m * rate, 2, MidpointRounding.AwayFromZero) : null;
        decimal? remaining = project.BudgetHours is { } budget ? Math.Round(budget - usedMinutes / 60m, 2) : null;
        return new ProjectUsage(project.Id, project.Name, project.Client, project.IsBillable, project.HourlyRate,
            project.CurrencyCode, project.BudgetHours, project.IsDeleted, periodMinutes, billable, amount, usedMinutes,
            Math.Round(usedMinutes / 60m, 2), remaining,
            project.BudgetHours is { } limit && usedMinutes > limit * 60);
    }

    /// <summary>The billable amounts added per currency; a project with no amount adds nothing.</summary>
    private static CurrencyAmount[] Amounts(IEnumerable<ProjectUsage> rows) => rows
        .Where(x => x is { BillableAmount: not null, CurrencyCode: not null })
        .GroupBy(x => x.CurrencyCode!).OrderBy(x => x.Key, StringComparer.Ordinal)
        .Select(x => new CurrencyAmount(x.Key, x.Sum(row => row.BillableAmount!.Value))).ToArray();

    private static UsageTotals Totals(ProjectUsage[] rows) =>
        new(rows.Sum(x => x.PeriodMinutes), rows.Sum(x => x.BillableMinutes), Amounts(rows));
}
