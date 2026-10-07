using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Aiyara.Identities.Api;

public interface IProfilePhotoStorage
{
    Task<string> PutAsync(Guid tenantId, Guid accountId, byte[] content, string contentType,
        CancellationToken cancellationToken);
    Task<ProfilePhotoContent?> GetAsync(Guid tenantId, Guid accountId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid tenantId, Guid accountId, CancellationToken cancellationToken);
}

public sealed record ProfilePhotoContent(byte[] Bytes, string ContentType);

public sealed class ProfilePhotoStorage(IAmazonS3 client, IConfiguration configuration) : IProfilePhotoStorage
{
    private string Bucket => configuration["ObjectStorage:Bucket"] ?? "aiyara-profile-photos";
    private static string Key(Guid tenantId, Guid accountId) =>
        $"profiles/{tenantId:N}/{accountId:N}/avatar";

    public async Task<string> PutAsync(Guid tenantId, Guid accountId, byte[] content,
        string contentType, CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
        var key = Key(tenantId, accountId);
        await using var stream = new MemoryStream(content, writable: false);
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType
        }, cancellationToken);
        return key;
    }

    public async Task<ProfilePhotoContent?> GetAsync(Guid tenantId, Guid accountId,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetObjectAsync(Bucket, Key(tenantId, accountId), cancellationToken);
        await using var content = new MemoryStream();
        await response.ResponseStream.CopyToAsync(content, cancellationToken);
        return new ProfilePhotoContent(content.ToArray(), response.Headers.ContentType ?? "image/jpeg");
    }

    public Task DeleteAsync(Guid tenantId, Guid accountId, CancellationToken cancellationToken) =>
        client.DeleteObjectAsync(Bucket, Key(tenantId, accountId), cancellationToken);

    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        var buckets = await client.ListBucketsAsync(cancellationToken);
        if (buckets.Buckets?.Any(bucket => bucket.BucketName == Bucket) == true) return;
        try
        {
            await client.PutBucketAsync(new PutBucketRequest { BucketName = Bucket }, cancellationToken);
        }
        catch (AmazonS3Exception exception) when
            (exception.ErrorCode is "BucketAlreadyExists" or "BucketAlreadyOwnedByYou")
        {
            // Another API instance created the bucket between the list and create calls.
        }
    }

    public static IAmazonS3 CreateClient(IConfiguration configuration)
    {
        var endpoint = configuration["ObjectStorage:Endpoint"] ?? "http://localhost:9000";
        var credentials = new BasicAWSCredentials(
            configuration["ObjectStorage:AccessKey"] ?? "",
            configuration["ObjectStorage:SecretKey"] ?? "");
        return new AmazonS3Client(credentials, new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
            UseHttp = endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        });
    }
}
