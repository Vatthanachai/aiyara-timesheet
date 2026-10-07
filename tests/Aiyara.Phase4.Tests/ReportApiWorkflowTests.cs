using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aiyara.Report.Api;
using Aiyara.Report.Databases;
using Aiyara.Report.Models;
using Aiyara.Report.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Aiyara.Phase4.Tests;

public sealed class ReportApiWorkflowTests
{
    [Fact]
    public async Task Authenticated_admin_can_request_report_upload_signed_pdf_and_download_latest_version()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var storage = new InMemoryReportObjectStorage();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddSingleton(connection);
        builder.Services.AddScoped<ReportingTenantScope>();
        builder.Services.AddDbContext<ReportingDbContext>((services, options) =>
            options.UseSqlite(services.GetRequiredService<Microsoft.Data.Sqlite.SqliteConnection>()));
        builder.Services.AddSingleton<IReportObjectStorage>(storage);
        builder.Services.AddSingleton<IReportAccessTokenValidator>(new FixedReportTokenValidator(tenantId, userId));
        await using var app = builder.Build();
        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.EnsureCreatedAsync();
        app.UseReportAuthentication();
        app.MapReportEndpoints();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            using var unauthorized = await client.GetAsync("/api/v1/context");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
            using var invalidToken = await client.GetAsync("/api/v1/context");
            Assert.Equal(HttpStatusCode.Unauthorized, invalidToken.StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "e2e-admin");

            var definitionResponse = await client.PostAsJsonAsync("/api/v1/definitions",
                new DefinitionRequest(ReportKind.Monthly, ReportFormat.Pdf, "Monthly report"));
            Assert.Equal(HttpStatusCode.Created, definitionResponse.StatusCode);
            using var definitionJson = JsonDocument.Parse(await definitionResponse.Content.ReadAsStringAsync());
            var definitionId = definitionJson.RootElement.GetProperty("id").GetGuid();
            var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
            var end = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
            var runResponse = await client.PostAsJsonAsync("/api/v1/runs",
                new RunRequest(definitionId, start, end, null));
            Assert.Equal(HttpStatusCode.Accepted, runResponse.StatusCode);
            using var runJson = JsonDocument.Parse(await runResponse.Content.ReadAsStringAsync());
            var runId = runJson.RootElement.GetProperty("id").GetGuid();

            var generatedPdf = ReportDocumentRenderer.RenderPdf(new ReportDocumentData("Employee",
                "Team", "September 2026", [], 0));
            const string generatedKey = "generated/report.pdf";
            await using (var generatedContent = new MemoryStream(generatedPdf))
                await storage.PutAsync(generatedKey, generatedContent, "application/pdf");
            await using (var scope = app.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ReportingTenantScope>().TenantId = tenantId;
                var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
                var run = await db.ReportRuns.SingleAsync(x => x.Id == runId);
                run.Status = ReportRunStatus.Succeeded;
                db.ReportObjects.Add(new ReportObject
                {
                    TenantId = tenantId, ReportRunId = runId, ObjectKey = generatedKey,
                    Sha256 = "generated", ContentType = "application/pdf", LengthBytes = generatedPdf.Length,
                    Version = 1, CreatedAtUtc = DateTime.UtcNow, RetainUntilUtc = DateTime.UtcNow.AddYears(7)
                });
                await db.SaveChangesAsync();
            }

            var signedPdf = "%PDF-1.7\nexternal signature\n%%EOF"u8.ToArray();
            using var multipart = new MultipartFormDataContent();
            var file = new ByteArrayContent(signedPdf);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            multipart.Add(file, "file", "signed.pdf");
            var uploadResponse = await client.PostAsync($"/api/v1/runs/{runId}/signed-document", multipart);
            Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
            using var uploadJson = JsonDocument.Parse(await uploadResponse.Content.ReadAsStringAsync());
            Assert.Equal(1, uploadJson.RootElement.GetProperty("version").GetInt32());

            var latestSignedPdf = "%PDF-1.7\nsecond signature version\n%%EOF"u8.ToArray();
            using var secondMultipart = new MultipartFormDataContent();
            secondMultipart.Add(new ByteArrayContent(latestSignedPdf), "file", "signed-v2.pdf");
            var secondUpload = await client.PostAsync($"/api/v1/runs/{runId}/signed-document", secondMultipart);
            Assert.Equal(HttpStatusCode.Created, secondUpload.StatusCode);
            using var secondUploadJson = JsonDocument.Parse(await secondUpload.Content.ReadAsStringAsync());
            Assert.Equal(2, secondUploadJson.RootElement.GetProperty("version").GetInt32());

            var download = await client.GetAsync($"/api/v1/runs/{runId}/download");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(latestSignedPdf, await download.Content.ReadAsByteArrayAsync());
            await using var auditScope = app.Services.CreateAsyncScope();
            auditScope.ServiceProvider.GetRequiredService<ReportingTenantScope>().TenantId = tenantId;
            var auditDb = auditScope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            Assert.Contains(await auditDb.Audits.ToListAsync(), x => x.Action == "signed-document.uploaded");
        }
        finally
        {
            await app.StopAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class InMemoryReportObjectStorage : IReportObjectStorage
    {
        private readonly Dictionary<string, (byte[] Bytes, string ContentType)> objects = new();
        public Task EnsureBucketAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task PutAsync(string key, Stream content, string contentType,
            CancellationToken cancellationToken = default)
        {
            await using var output = new MemoryStream();
            await content.CopyToAsync(output, cancellationToken);
            objects[key] = (output.ToArray(), contentType);
        }
        public Task<ReportObjectDownload> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            var stored = objects[key];
            return Task.FromResult(new ReportObjectDownload(new MemoryStream(stored.Bytes), stored.ContentType));
        }
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            objects.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedReportTokenValidator(Guid tenantId, Guid userId) : IReportAccessTokenValidator
    {
        public Task<ReportTokenValidation> ValidateAsync(string accessToken, string correlationId,
            CancellationToken cancellationToken) => Task.FromResult(new ReportTokenValidation(
                accessToken == "e2e-admin", tenantId.ToString(), userId.ToString(),
                "Asia/Bangkok", ["TenantAdmin"]));
    }
}
