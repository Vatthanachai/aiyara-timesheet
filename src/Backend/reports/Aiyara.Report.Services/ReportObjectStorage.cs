using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;

namespace Aiyara.Report.Services;

public sealed class ReportObjectStorage(IAmazonS3 client, IConfiguration configuration) : IReportObjectStorage
{
    private static readonly Meter Meter = new("Aiyara.Report.Storage");
    private static readonly Counter<long> Operations = Meter.CreateCounter<long>(
        "aiyara.rustfs.operations", description: "Report object storage operation outcomes.");
    private string Bucket => configuration["ObjectStorage:Bucket"] ?? "aiyara-reports";

    public async Task EnsureBucketAsync(CancellationToken cancellationToken = default)
    {
        await MeasureAsync("ensure_bucket", async () =>
        {
            var buckets = await client.ListBucketsAsync(cancellationToken);
            if (buckets.Buckets?.Any(x => x.BucketName == Bucket) == true) return;
            await client.PutBucketAsync(new PutBucketRequest { BucketName = Bucket }, cancellationToken);
        });
    }

    public async Task PutAsync(string key, Stream content, string contentType,
        CancellationToken cancellationToken = default)
    {
        await MeasureAsync("put", async () => await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Bucket, Key = key, InputStream = content, ContentType = contentType
        }, cancellationToken));
    }

    public async Task<ReportObjectDownload> GetAsync(string key,
        CancellationToken cancellationToken = default)
    {
        return await MeasureAsync("get", async () =>
        {
            var response = await client.GetObjectAsync(Bucket, key, cancellationToken);
            return new ReportObjectDownload(response.ResponseStream,
                response.Headers.ContentType ?? "application/octet-stream", response);
        });
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        await MeasureAsync("delete", async () => await client.DeleteObjectAsync(Bucket, key, cancellationToken));

    private static async Task MeasureAsync(string operation, Func<Task> callback)
    {
        try
        {
            await callback();
            Operations.Add(1, new KeyValuePair<string, object?>("operation", operation),
                new KeyValuePair<string, object?>("outcome", "success"));
        }
        catch
        {
            Operations.Add(1, new KeyValuePair<string, object?>("operation", operation),
                new KeyValuePair<string, object?>("outcome", "failure"));
            throw;
        }
    }

    private static async Task<T> MeasureAsync<T>(string operation, Func<Task<T>> callback)
    {
        try
        {
            var result = await callback();
            Operations.Add(1, new KeyValuePair<string, object?>("operation", operation),
                new KeyValuePair<string, object?>("outcome", "success"));
            return result;
        }
        catch
        {
            Operations.Add(1, new KeyValuePair<string, object?>("operation", operation),
                new KeyValuePair<string, object?>("outcome", "failure"));
            throw;
        }
    }

    public static IAmazonS3 CreateClient(IConfiguration configuration)
    {
        var endpoint = configuration["ObjectStorage:Endpoint"] ?? "http://localhost:9000";
        var credentials = new BasicAWSCredentials(
            configuration["ObjectStorage:AccessKey"] ?? "",
            configuration["ObjectStorage:SecretKey"] ?? "");
        var clientConfig = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
            UseHttp = endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        };
        return new AmazonS3Client(credentials, clientConfig);
    }
}
