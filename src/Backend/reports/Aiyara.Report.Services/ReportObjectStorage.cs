using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

namespace Aiyara.Report.Services;

public sealed class ReportObjectStorage(IAmazonS3 client, IConfiguration configuration)
{
    private string Bucket => configuration["ObjectStorage:Bucket"] ?? "aiyara-reports";

    public async Task EnsureBucketAsync(CancellationToken cancellationToken = default)
    {
        var buckets = await client.ListBucketsAsync(cancellationToken);
        if (buckets.Buckets.Any(x => x.BucketName == Bucket)) return;
        await client.PutBucketAsync(new PutBucketRequest { BucketName = Bucket }, cancellationToken);
    }

    public async Task PutAsync(string key, Stream content, string contentType,
        CancellationToken cancellationToken = default)
    {
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Bucket, Key = key, InputStream = content, ContentType = contentType
        }, cancellationToken);
    }

    public async Task<GetObjectResponse> GetAsync(string key,
        CancellationToken cancellationToken = default) =>
        await client.GetObjectAsync(Bucket, key, cancellationToken);

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        await client.DeleteObjectAsync(Bucket, key, cancellationToken);

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
