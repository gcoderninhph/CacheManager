using System;
using System.Text.Json;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace CacheManager.Core;

internal sealed partial class RedisMap<TKey, TValue>
{
    private async Task UpdateAccessTimeAsync(TKey key)
    {
        var db = _redis.GetDatabase(_database);
        var accessTimeKey = GetAccessTimeKey();
        var fieldName = SerializeKey(key);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await db.SortedSetAddAsync(accessTimeKey, fieldName, now);
    }

    private void ProcessExpiration(object? state)
    {
        _ = ProcessExpirationAsync();
    }

    private async Task ProcessExpirationAsync()
    {
        var ttl = await GetItemTtlFromRedisAsync();
        if (!ttl.HasValue)
        {
            return;
        }

        try
        {
            var db = _redis.GetDatabase(_database);
            var accessTimeKey = GetAccessTimeKey();
            var hashKey = GetHashKey();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expirationThreshold = now - (long)ttl.Value.TotalSeconds;

            var expiredKeys = await db.SortedSetRangeByScoreAsync(
                accessTimeKey,
                double.NegativeInfinity,
                expirationThreshold);

            if (expiredKeys.Length == 0)
            {
                return;
            }

            foreach (var expiredKeyValue in expiredKeys)
            {
                TValue? expiredValue = default;
                var hasValue = false;

                try
                {
                    var serializedKey = expiredKeyValue.ToString();
                    var (found, value) = await TryReadValueWithLeaseAsync(db, hashKey, serializedKey);

                    if (!found)
                    {
                        await db.SortedSetRemoveAsync(accessTimeKey, serializedKey);
                        continue;
                    }

                    expiredValue = value;
                    hasValue = true;

                    await db.HashDeleteAsync(hashKey, serializedKey);
                    await db.SortedSetRemoveAsync(accessTimeKey, serializedKey);

                    if (hasValue && TryDeserializeKey(serializedKey, out var key, out _) && key != null)
                    {
                        await RemoveVersionFromRedisAsync(key);
                        await TriggerExpiredHandlers(key, expiredValue!);
                        await TriggerRemoveHandlers(key, expiredValue!);
                    }
                }
                catch
                {
                    // Ignore individual key errors
                }
            }
        }
        catch
        {
            // Ignore timer errors
        }
    }
}
