using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using TheWatch.Contracts.Caching;

namespace TheWatch.Infrastructure.Adapters.Persistence;

public class RedisDistributedCacheAdapter : ICacheStore
{
    private sealed record CacheEnvelope<T>(T? Value, long? SlidingMilliseconds);
    private readonly IConnectionMultiplexer _redis;

    public RedisDistributedCacheAdapter(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(value);
        await db.StringSetAsync(key, json, expiry ?? TimeSpan.FromMinutes(30));
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(key);
        if (value.IsNullOrEmpty) return default;
        return JsonSerializer.Deserialize<T>(value!);
    }

    async ValueTask<CacheRead<T>> ICacheStore.GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        var database = _redis.GetDatabase();
        var value = await database.StringGetAsync(key).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (value.IsNullOrEmpty) return CacheRead<T>.Miss;
        var envelope = JsonSerializer.Deserialize<CacheEnvelope<T>>(value!)
            ?? throw new JsonException($"Cache entry '{key}' has no payload.");
        if (envelope.SlidingMilliseconds is { } milliseconds)
        {
            await database.KeyExpireAsync(key, TimeSpan.FromMilliseconds(milliseconds)).ConfigureAwait(false);
        }
        return CacheRead<T>.Hit(envelope.Value);
    }

    async ValueTask ICacheStore.SetAsync<T>(string key, T value, CacheEntryOptions? options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        var expiration = Minimum(options?.AbsoluteExpirationRelativeToNow, options?.SlidingExpiration);
        var envelope = new CacheEnvelope<T>(value, options?.SlidingExpiration?.TotalMilliseconds is { } sliding ? (long)sliding : null);
        var json = JsonSerializer.Serialize(envelope);
        await _redis.GetDatabase().StringSetAsync(key, json, expiration).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    async ValueTask<bool> ICacheStore.RemoveAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        var removed = await _redis.GetDatabase().KeyDeleteAsync(key).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return removed;
    }

    private static TimeSpan? Minimum(TimeSpan? first, TimeSpan? second)
    {
        if (first is null) return second;
        if (second is null) return first;
        return first <= second ? first : second;
    }
}
