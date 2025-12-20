using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace CacheManager.Core;

internal sealed partial class RedisMap<TKey, TValue>
{
    private string GetHashKey() => $"map:{_mapName}";

    private string GetAccessTimeKey() => $"map:{_mapName}:access-time";

    private string GetVersionsKey() => $"map:{_mapName}:__meta:versions";

    private string GetTimestampsKey() => $"map:{_mapName}:__meta:timestamps";

    private string GetTimestampsSortedSetKey() => $"map:{_mapName}:__meta:timestamps-sorted";

    private string GetTtlConfigKey() => $"map:{_mapName}:__meta:ttl-config";

    private string SerializeKey(TKey key) => JsonSerializer.Serialize(key, JsonOptions);

    private bool TryDeserializeKey(RedisValue rawKey, out TKey? key, out string keyString)
    {
        try
        {
            key = JsonSerializer.Deserialize<TKey>(rawKey.ToString(), JsonOptions);
            keyString = key?.ToString() ?? string.Empty;
            return key != null;
        }
        catch
        {
            key = default;
            keyString = string.Empty;
            return false;
        }
    }

    private PooledRedisValue SerializeValue(TValue value) => _valueFormatter.Serialize(value);

    private TValue DeserializeValue(ReadOnlySpan<byte> data) => _valueFormatter.Deserialize(data);

    private string FormatValueForDisplay(TValue value) => _valueFormatter.ToDisplayString(value);

    private void ReturnValueToPoolIfNeeded(TValue value)
    {
        if (!_valueFormatter.SupportsPooling || value is null)
        {
            return;
        }

        _valueFormatter.ReturnToPool(value);
    }

    private async Task<Guid> GetVersionFromRedisAsync(TKey key)
    {
        var db = _redis.GetDatabase(_database);
        var versionsKey = GetVersionsKey();
        var fieldName = SerializeKey(key);
        var versionStr = await db.HashGetAsync(versionsKey, fieldName);

        if (versionStr.HasValue && Guid.TryParse(versionStr!, out var version))
        {
            return version;
        }

        var newVersion = Guid.NewGuid();
        await db.HashSetAsync(versionsKey, fieldName, newVersion.ToString());
        return newVersion;
    }

    private async Task SetVersionInRedisAsync(TKey key, Guid version)
    {
        var db = _redis.GetDatabase(_database);
        var versionsKey = GetVersionsKey();
        var fieldName = SerializeKey(key);
        await db.HashSetAsync(versionsKey, fieldName, version.ToString());
    }

    private async Task<DateTime> GetTimestampFromRedisAsync(TKey key)
    {
        var db = _redis.GetDatabase(_database);
        var timestampsKey = GetTimestampsKey();
        var fieldName = SerializeKey(key);
        var timestampStr = await db.HashGetAsync(timestampsKey, fieldName);

        if (timestampStr.HasValue && long.TryParse(timestampStr!, out var ticks))
        {
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        return DateTime.UtcNow;
    }

    private async Task SetTimestampInRedisAsync(TKey key, DateTime timestamp)
    {
        var db = _redis.GetDatabase(_database);
        var fieldName = SerializeKey(key);

        var timestampsKey = GetTimestampsKey();
        await db.HashSetAsync(timestampsKey, fieldName, timestamp.Ticks);

        var sortedSetKey = GetTimestampsSortedSetKey();
        await db.SortedSetAddAsync(sortedSetKey, fieldName, timestamp.Ticks);
    }

    private string FormatTimeAgo(DateTime timestamp)
    {
        var now = DateTime.UtcNow;
        var diff = now - timestamp;

        if (diff.TotalSeconds < 60)
        {
            return $"{(int)diff.TotalSeconds}s ago";
        }

        if (diff.TotalMinutes < 60)
        {
            return $"{(int)diff.TotalMinutes}m ago";
        }

        if (diff.TotalHours < 24)
        {
            return $"{(int)diff.TotalHours}h ago";
        }

        if (diff.TotalDays < 30)
        {
            return $"{(int)diff.TotalDays}d ago";
        }

        if (diff.TotalDays < 365)
        {
            return $"{(int)(diff.TotalDays / 30)}mo ago";
        }

        return $"{(int)(diff.TotalDays / 365)}y ago";
    }

    private async Task<TimeSpan?> GetItemTtlFromRedisAsync()
    {
        var db = _redis.GetDatabase(_database);
        var ttlConfigKey = GetTtlConfigKey();
        var ttlSeconds = await db.StringGetAsync(ttlConfigKey);

        if (ttlSeconds.HasValue && double.TryParse(ttlSeconds!, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    private async Task SetItemTtlInRedisAsync(TimeSpan? ttl)
    {
        var db = _redis.GetDatabase(_database);
        var ttlConfigKey = GetTtlConfigKey();

        if (ttl.HasValue)
        {
            await db.StringSetAsync(ttlConfigKey, ttl.Value.TotalSeconds);
        }
        else
        {
            await db.KeyDeleteAsync(ttlConfigKey);
        }
    }

    private async Task RemoveVersionFromRedisAsync(TKey key)
    {
        var db = _redis.GetDatabase(_database);
        var fieldName = SerializeKey(key);

        await db.HashDeleteAsync(GetVersionsKey(), fieldName);
        await db.HashDeleteAsync(GetTimestampsKey(), fieldName);
        await db.SortedSetRemoveAsync(GetTimestampsSortedSetKey(), fieldName);
    }

    private async Task ClearAllVersionMetadataAsync()
    {
        var db = _redis.GetDatabase(_database);

        await db.KeyDeleteAsync(GetVersionsKey());
        await db.KeyDeleteAsync(GetTimestampsKey());
        await db.KeyDeleteAsync(GetTimestampsSortedSetKey());
    }

    private async Task<Dictionary<TKey, Guid>> GetAllVersionsFromRedisAsync()
    {
        var db = _redis.GetDatabase(_database);
        var versionsKey = GetVersionsKey();
        var entries = await db.HashGetAllAsync(versionsKey);

        var result = new Dictionary<TKey, Guid>();
        foreach (var entry in entries)
        {
            try
            {
                if (!TryDeserializeKey(entry.Name, out var key, out _))
                {
                    continue;
                }

                if (key != null && Guid.TryParse(entry.Value!, out var version))
                {
                    result[key] = version;
                }
            }
            catch
            {
                // Skip invalid entries
            }
        }

        return result;
    }
}
