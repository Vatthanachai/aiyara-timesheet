using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aiyara.Timesheet.Contracts.Identity.V1;
using Google.Protobuf;
using StackExchange.Redis;

internal sealed class RedisIdentityValidationCache(IConfiguration configuration) : IAsyncDisposable
{
    private sealed record Entry(string Response, string AccountVersion, string TenantPolicyEpoch);
    private readonly Lazy<Task<ConnectionMultiplexer>> connection = new(() =>
    {
        var options = ConfigurationOptions.Parse(configuration["Redis:ConnectionString"]
            ?? "localhost:6379");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 1000;
        options.AsyncTimeout = 1000;
        return ConnectionMultiplexer.ConnectAsync(options);
    });

    public async Task<ValidateAccessTokenResponse?> GetAsync(string token,
        CancellationToken cancellationToken)
    {
        try
        {
            var redis = await connection.Value.WaitAsync(cancellationToken);
            var db = redis.GetDatabase();
            var cacheKey = await db.StringGetAsync(TokenLookupKey(token));
            if (cacheKey.IsNullOrEmpty) return null;
            var stored = await db.StringGetAsync((RedisKey)(string)cacheKey!);
            if (stored.IsNullOrEmpty) return null;
            var entry = JsonSerializer.Deserialize<Entry>((string)stored!);
            if (entry is null) return null;
            var response = ValidateAccessTokenResponse.Parser.ParseFrom(
                Convert.FromBase64String(entry.Response));
            if (!response.IsValid || response.ExpiresAtUtc.ToDateTimeOffset() <= DateTimeOffset.UtcNow ||
                !Guid.TryParse(response.SubjectId, out var accountId) ||
                !Guid.TryParse(response.TenantId, out var tenantId)) return null;
            var versions = await db.StringGetAsync([
                IdentityCacheKeys.AccountVersion(tenantId, accountId),
                IdentityCacheKeys.TenantPolicyEpoch(tenantId)]);
            if (PolicyVersionChanged(versions[1].ToString(), response) ||
                (long.TryParse(versions[0].ToString(), out var minimumVersion) &&
                 response.SessionVersion < minimumVersion)) return null;
            return versions[0].ToString() == entry.AccountVersion &&
                versions[1].ToString() == entry.TenantPolicyEpoch ? response : null;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException or
            JsonException or FormatException or InvalidProtocolBufferException)
        {
            return null;
        }
    }

    public async Task<bool> SetAsync(string token, ValidateAccessTokenResponse response,
        CancellationToken cancellationToken)
    {
        if (!response.IsValid || !Guid.TryParse(response.SubjectId, out var accountId) ||
            !Guid.TryParse(response.TenantId, out var tenantId)) return false;
        try
        {
            var redis = await connection.Value.WaitAsync(cancellationToken);
            var db = redis.GetDatabase();
            var versions = await db.StringGetAsync([
                IdentityCacheKeys.AccountVersion(tenantId, accountId),
                IdentityCacheKeys.TenantPolicyEpoch(tenantId)]);
            if (PolicyVersionChanged(versions[1].ToString(), response) ||
                (long.TryParse(versions[0].ToString(), out var minimumVersion) &&
                 response.SessionVersion < minimumVersion)) return false;
            var entry = new Entry(Convert.ToBase64String(response.ToByteArray()),
                versions[0].ToString(), versions[1].ToString());
            var ttl = response.ExpiresAtUtc.ToDateTimeOffset() - DateTimeOffset.UtcNow;
            if (ttl <= TimeSpan.Zero) return false;
            if (ttl > TimeSpan.FromMinutes(1)) ttl = TimeSpan.FromMinutes(1);
            var cacheKey = ValidationKey(response.TokenId, response.SessionVersion);
            await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(entry), ttl);
            await db.StringSetAsync(TokenLookupKey(token), cacheKey, ttl);
            return true;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            // Identity gRPC remains authoritative when Redis is unavailable.
            return true;
        }
    }

    private static bool PolicyVersionChanged(string marker,
        ValidateAccessTokenResponse response) => marker == "blocked" ||
        (marker.Length > 0 && marker != response.PolicyVersion.ToString(
            System.Globalization.CultureInfo.InvariantCulture));

    private static string TokenLookupKey(string token)
        => "identity:validation:lookup:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string ValidationKey(string tokenId, long sessionVersion)
        => $"identity:validation:{tokenId}:{sessionVersion}";

    public async ValueTask DisposeAsync()
    {
        if (connection.IsValueCreated)
            (await connection.Value).Dispose();
    }
}
