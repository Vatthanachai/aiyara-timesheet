using Aiyara.Report.Databases;
using Aiyara.Report.Models;
using Aiyara.Report.Services;
using Aiyara.Report.Worker;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aiyara.Phase4.Tests;

public sealed class ReportRetentionTests
{
    [Fact]
    public async Task Purge_deletes_expired_objects_and_only_removes_snapshots_without_retained_versions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var tenantId = Guid.NewGuid();
        var tenantScope = new ReportingTenantScope { TenantId = tenantId };
        var services = new ServiceCollection();
        services.AddSingleton(tenantScope);
        services.AddSingleton(connection);
        services.AddDbContext<ReportingDbContext>((provider, options) =>
            options.UseSqlite(provider.GetRequiredService<SqliteConnection>()));
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            await db.Database.EnsureCreatedAsync();
            var definition = new ReportDefinition
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Kind = ReportKind.Monthly, Format = ReportFormat.Pdf,
                Name = "Retention test", CreatedAtUtc = DateTime.UtcNow
            };
            var noRetainedRun = NewRun(tenantId, definition.Id, "expired-only");
            var retainedRun = NewRun(tenantId, definition.Id, "has-retained-version");
            db.ReportDefinitions.Add(definition);
            db.ReportRuns.AddRange(noRetainedRun, retainedRun);
            db.ReportSnapshots.AddRange(NewSnapshot(noRetainedRun), NewSnapshot(retainedRun));
            db.ReportObjects.AddRange(
                NewObject(noRetainedRun, "expired-only.pdf", 1, DateTime.UtcNow.AddDays(-1)),
                NewObject(retainedRun, "expired-version.pdf", 1, DateTime.UtcNow.AddDays(-1)),
                NewObject(retainedRun, "retained-version.pdf", 2, DateTime.UtcNow.AddYears(1)));
            await db.SaveChangesAsync();
        }

        await using (var immutabilityScope = provider.CreateAsyncScope())
        {
            var db = immutabilityScope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var snapshot = await db.ReportSnapshots.SingleAsync(x => x.PayloadJson == "expired-only");
            snapshot.PayloadJson = "tampered";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        var storage = new RecordingStorage();
        var job = new RetentionPurgeJob(provider.GetRequiredService<IServiceScopeFactory>(), storage);
        await job.ExecuteAsync(CancellationToken.None);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        Assert.Equal(new[] { "expired-only.pdf", "expired-version.pdf" }, storage.Deleted.Order().ToArray());
        Assert.Equal(new[] { "retained-version.pdf" },
            await verifyDb.ReportObjects.Select(x => x.ObjectKey).ToArrayAsync());
        Assert.Equal(new[] { "has-retained-version" },
            await verifyDb.ReportSnapshots.Select(x => x.PayloadJson).ToArrayAsync());
        var audits = await verifyDb.Audits.Select(x => x.Action).ToListAsync();
        Assert.Equal(2, audits.Count(x => x == "object.purged.retention"));
        Assert.Single(audits, x => x == "snapshot.purged.retention");
    }

    private static ReportRun NewRun(Guid tenantId, Guid definitionId, string key) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, ReportDefinitionId = definitionId, RequestedBy = Guid.NewGuid(),
        TimeZoneId = "UTC", IdempotencyKey = key, Status = ReportRunStatus.Succeeded,
        PeriodStartUtc = DateTime.UtcNow.AddMonths(-1), PeriodEndUtc = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow
    };

    private static ReportSnapshot NewSnapshot(ReportRun run) => new()
    {
        TenantId = run.TenantId, ReportRunId = run.Id, PayloadJson = run.IdempotencyKey,
        Sha256 = new string('a', 64), CreatedAtUtc = DateTime.UtcNow
    };

    private static ReportObject NewObject(ReportRun run, string key, int version, DateTime retainUntil) => new()
    {
        TenantId = run.TenantId, ReportRunId = run.Id, ObjectKey = key,
        Sha256 = new string('b', 64), ContentType = "application/pdf", Version = version,
        LengthBytes = 10, CreatedAtUtc = DateTime.UtcNow, RetainUntilUtc = retainUntil
    };

    private sealed class RecordingStorage : IReportObjectStorage
    {
        public List<string> Deleted { get; } = [];
        public Task EnsureBucketAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PutAsync(string key, Stream content, string contentType,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ReportObjectDownload> GetAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Deleted.Add(key);
            return Task.CompletedTask;
        }
    }
}
