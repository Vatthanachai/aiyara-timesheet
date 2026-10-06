using System.Security.Cryptography;
using System.Text;
using Aiyara.Identities.Services.Authentication;
using StackExchange.Redis;

internal sealed class RedisCredentialAttemptLimiter(IConfiguration configuration)
    : ICredentialAttemptLimiter, IAsyncDisposable
{
    private const string IncrementScript = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then redis.call('EXPIRE', KEYS[1], 60) end
        return count
        """;

    private readonly Lazy<Task<ConnectionMultiplexer>> connection = new(() =>
    {
        var options = ConfigurationOptions.Parse(configuration["Redis:ConnectionString"]
            ?? "localhost:6379");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 1000;
        options.AsyncTimeout = 1000;
        return ConnectionMultiplexer.ConnectAsync(options);
    });

    public async Task CheckAsync(Guid tenantId, string normalizedEmail, string purpose,
        CancellationToken cancellationToken)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{tenantId:N}:{normalizedEmail}")));
        var key = (RedisKey)$"identity:attempt:{purpose}:{digest}";
        try
        {
            var redis = await connection.Value.WaitAsync(cancellationToken);
            var count = (long)await redis.GetDatabase().ScriptEvaluateAsync(
                IncrementScript, [key], []);
            var limit = purpose == "Login" ? 10 : 5;
            if (count > limit)
                throw new AuthenticationException(AuthenticationFailure.RateLimited,
                    "Too many authentication attempts. Try again later.");
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            throw new AuthenticationException(AuthenticationFailure.Unavailable,
                "Authentication rate limiting is unavailable.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (connection.IsValueCreated)
            (await connection.Value).Dispose();
    }
}
