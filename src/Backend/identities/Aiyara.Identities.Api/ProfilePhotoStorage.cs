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

/// <summary>Each organization's logo, one object per tenant.</summary>
public interface IOrganizationLogoStorage
{
    Task<string> PutLogoAsync(Guid tenantId, byte[] content, string contentType, CancellationToken cancellationToken);
    Task<ProfilePhotoContent?> GetLogoAsync(string key, CancellationToken cancellationToken);
    Task DeleteLogoAsync(string key, CancellationToken cancellationToken);
}

public sealed class ProfilePhotoStorage(IAmazonS3 client, IConfiguration configuration)
    : IProfilePhotoStorage, IOrganizationLogoStorage
{
    private string Bucket => configuration["ObjectStorage:Bucket"] ?? "aiyara-profile-photos";
    private static string Key(Guid tenantId, Guid accountId) =>
        $"profiles/{tenantId:N}/{accountId:N}/avatar";

    public Task<string> PutAsync(Guid tenantId, Guid accountId, byte[] content,
        string contentType, CancellationToken cancellationToken) =>
        PutObjectAsync(Key(tenantId, accountId), content, contentType, cancellationToken);

    public Task<string> PutLogoAsync(Guid tenantId, byte[] content, string contentType,
        CancellationToken cancellationToken) =>
        PutObjectAsync($"organizations/{tenantId:N}/logo", content, contentType, cancellationToken);

    public async Task<ProfilePhotoContent?> GetLogoAsync(string key, CancellationToken cancellationToken)
    {
        using var response = await client.GetObjectAsync(Bucket, key, cancellationToken);
        await using var content = new MemoryStream();
        await response.ResponseStream.CopyToAsync(content, cancellationToken);
        return new ProfilePhotoContent(content.ToArray(), response.Headers.ContentType ?? "image/png");
    }

    public Task DeleteLogoAsync(string key, CancellationToken cancellationToken) =>
        client.DeleteObjectAsync(Bucket, key, cancellationToken);

    private async Task<string> PutObjectAsync(string key, byte[] content, string contentType,
        CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
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
