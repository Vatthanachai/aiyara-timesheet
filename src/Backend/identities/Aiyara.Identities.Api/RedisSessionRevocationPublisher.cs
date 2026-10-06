using Aiyara.Identities.Services.Authentication;
using Aiyara.Timesheet.Contracts.Identity.V1;
using StackExchange.Redis;

internal sealed class RedisSessionRevocationPublisher(IConfiguration configuration)
    : ISessionRevocationPublisher, IAsyncDisposable
{
    private readonly Lazy<Task<ConnectionMultiplexer>> connection = new(() =>
    {
        var options = ConfigurationOptions.Parse(configuration["Redis:ConnectionString"]
            ?? "localhost:6379");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 1000;
        options.AsyncTimeout = 1000;
        return ConnectionMultiplexer.ConnectAsync(options);
    });

    public async Task PublishAccountAsync(Guid tenantId, Guid accountId, long sessionVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var redis = await connection.Value.WaitAsync(cancellationToken);
            await redis.GetDatabase().StringSetAsync(
                IdentityCacheKeys.AccountVersion(tenantId, accountId),
                sessionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                TimeSpan.FromHours(2));
            await redis.GetSubscriber().PublishAsync(
                RedisChannel.Literal(IdentityCacheKeys.RevocationsChannel),
                $"account:{tenantId:N}:{accountId:N}:{sessionVersion}");
        }
        catch (RedisException)
        {
            throw new AuthenticationException(AuthenticationFailure.Unavailable,
                "Session revocation is unavailable.");
        }
    }

    public async Task PublishTenantPolicyAsync(Guid tenantId,
        CancellationToken cancellationToken)
    {
        try
        {
            var redis = await connection.Value.WaitAsync(cancellationToken);
            await redis.GetDatabase().StringSetAsync(
                IdentityCacheKeys.TenantPolicyEpoch(tenantId), "blocked",
                TimeSpan.FromHours(2));
            await redis.GetSubscriber().PublishAsync(
                RedisChannel.Literal(IdentityCacheKeys.RevocationsChannel),
                $"tenant-policy:{tenantId:N}:blocked");
        }
        catch (RedisException)
        {
            throw new AuthenticationException(AuthenticationFailure.Unavailable,
                "Session revocation is unavailable.");
        }
    }

    public async Task CompleteTenantPolicyAsync(Guid tenantId,
        CancellationToken cancellationToken)
    {
        try
        {
            var redis = await connection.Value.WaitAsync(cancellationToken);
            var epoch = Guid.NewGuid().ToString("N");
            await redis.GetDatabase().StringSetAsync(
                IdentityCacheKeys.TenantPolicyEpoch(tenantId), epoch,
                TimeSpan.FromHours(2));
            await redis.GetSubscriber().PublishAsync(
                RedisChannel.Literal(IdentityCacheKeys.RevocationsChannel),
                $"tenant-policy:{tenantId:N}:{epoch}");
        }
        catch (RedisException)
        {
            throw new AuthenticationException(AuthenticationFailure.Unavailable,
                "Session revocation is unavailable.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (connection.IsValueCreated)
            (await connection.Value).Dispose();
    }
}
