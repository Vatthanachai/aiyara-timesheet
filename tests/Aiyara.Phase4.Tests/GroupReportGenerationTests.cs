using System.Net;
using System.Text;
using System.Text.Json;
using Aiyara.Report.Databases;
using Aiyara.Report.Models;
using Aiyara.Report.Services;
using Aiyara.Report.Worker;
using Aiyara.Timesheet.Contracts.Identity.V1;
using Aiyara.Timesheet.Contracts.Messaging.V1;
using Grpc.Net.Client;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aiyara.Phase4.Tests;

/// <summary>A member group report reads the organization's locked month from Timesheet and keeps
/// only the members the run was requested for.</summary>
public sealed class GroupReportGenerationTests
{
    [Fact]
    public async Task A_group_report_contains_only_the_groups_members()
    {
        var tenantId = Guid.NewGuid();
        var (ada, ben, outsider) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddScoped<ReportingTenantScope>();
        services.AddDbContext<ReportingDbContext>(options => options.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();

        var definition = new ReportDefinition { Id = Guid.NewGuid(), TenantId = tenantId, Kind = ReportKind.Monthly,
            Format = ReportFormat.Xlsx, Name = "Monthly", CreatedAtUtc = DateTime.UtcNow };
        var run = new ReportRun { Id = Guid.NewGuid(), TenantId = tenantId, ReportDefinitionId = definition.Id,
            SubjectGroupName = "Outsource", SubjectGroupMemberIdsJson = JsonSerializer.Serialize(new[] { ada, ben }),
            RequestedBy = Guid.NewGuid(), TimeZoneId = "UTC", IdempotencyKey = "group-test",
            PeriodStartUtc = new DateTime(2026, 9, 1), PeriodEndUtc = new DateTime(2026, 10, 1),
            CreatedAtUtc = DateTime.UtcNow };
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ReportingTenantScope>().TenantId = tenantId;
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.ReportDefinitions.Add(definition);
            db.ReportRuns.Add(run);
            await db.SaveChangesAsync();
        }

        var timesheet = new SnapshotHandler(new[] { (ada, "Ada task"), (ben, "Ben task"), (outsider, "Outsider task") });
        var storage = new MemoryStorage();
        using var channel = GrpcChannel.ForAddress("http://127.0.0.1:1");
        var consumer = new ReportGenerationConsumer(provider.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(), storage, new Clients(timesheet),
            new IdentityValidationService.IdentityValidationServiceClient(channel),
            NullLogger<ReportGenerationConsumer>.Instance);

        await consumer.ProcessAsync(new MessageEnvelope<ReportGenerationRequestedV1>(Guid.NewGuid(), Guid.NewGuid(),
            "group-test", tenantId, DateTime.UtcNow, "report.generate",
            new ReportGenerationRequestedV1(tenantId, run.Id, "Monthly")), default);

        // The organization-wide month was read once, not once per member.
        Assert.Equal(["/internal/v1/report-snapshots/2026/9"], timesheet.Paths);
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ReportingTenantScope>().TenantId = tenantId;
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            Assert.Equal(ReportRunStatus.Succeeded, (await db.ReportRuns.SingleAsync()).Status);
            var payload = (await db.ReportSnapshots.SingleAsync()).PayloadJson;
            Assert.Contains("Ada task", payload);
            Assert.Contains("Ben task", payload);
            Assert.DoesNotContain("Outsider task", payload);
            Assert.Contains("\"subjectGroupName\":\"Outsource\"", payload);
        }
        var document = Encoding.UTF8.GetString(DecompressedXlsx(storage.Objects.Values.Single()));
        Assert.Contains("Group summary (2 members)", document);
        Assert.Contains("Outsource", document);
        Assert.DoesNotContain("Outsider task", document);
    }

    /// <summary>The text of every part of an .xlsx package.</summary>
    private static byte[] DecompressedXlsx(byte[] xlsx)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(xlsx));
        using var all = new MemoryStream();
        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            stream.CopyTo(all);
        }
        return all.ToArray();
    }

    private sealed class SnapshotHandler((Guid Owner, string Task)[] owners) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            var result = owners.Select(x => new
            {
                OwnerId = x.Owner,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    entries = new[] { new { Date = "2026-09-07", StartTime = "09:00", EndTime = "10:00",
                        DurationMinutes = 60, TaskName = x.Task } },
                    leaves = Array.Empty<object>()
                }),
                CreatedAtUtc = DateTime.UtcNow
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { result }), Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class Clients(HttpMessageHandler timesheet) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(timesheet, disposeHandler: false) { BaseAddress = new Uri("http://timesheet") };
    }

    private sealed class MemoryStorage : IReportObjectStorage
    {
        public Dictionary<string, byte[]> Objects { get; } = [];
        public Task EnsureBucketAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            Objects[key] = copy.ToArray();
        }
        public Task<ReportObjectDownload> GetAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
