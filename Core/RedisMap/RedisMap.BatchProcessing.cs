using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace CacheManager.Core;

internal sealed partial class RedisMap<TKey, TValue>
{
    private void ProcessBatch(object? state)
    {
        if (_onBatchUpdateHandlers.Count == 0)
        {
            return;
        }

        _ = ProcessBatchAsync();
    }

    private async Task ProcessBatchAsync()
    {
        try
        {
            var now = DateTime.UtcNow;
            var batch = new List<IEntry<TKey, TValue>>();

            var db = _redis.GetDatabase(_database);
            var sortedSetKey = GetTimestampsSortedSetKey();
            var sortedSetExists = await db.KeyExistsAsync(sortedSetKey);

            if (sortedSetExists)
            {
                await ProcessBatchAsync_Optimized(now, batch, db);
            }
            else
            {
                await ProcessBatchAsync_Legacy(now, batch, db);
            }

            if (batch.Count > 0)
            {
                var lastBatchKey = $"{GetTimestampsKey()}:last-batch";
                await db.StringSetAsync(lastBatchKey, now.Ticks);

                await TriggerBatchUpdateHandlers(batch);
            }
        }
        catch
        {
            // Ignore batch processing errors
        }
    }

    private async Task ProcessBatchAsync_Optimized(DateTime now, List<IEntry<TKey, TValue>> batch, IDatabase db)
    {
        var sortedSetKey = GetTimestampsSortedSetKey();
        var lastBatchKey = $"{GetTimestampsKey()}:last-batch";
        var lastBatchTime = await db.StringGetAsync(lastBatchKey);
        long lastBatchTicks = DateTime.MinValue.Ticks;

        if (lastBatchTime.HasValue && long.TryParse(lastBatchTime!, out var ticks))
        {
            lastBatchTicks = ticks;
        }

        var minScore = lastBatchTicks;
        var maxScore = now.Add(-_batchWaitTime).Ticks;

        var results = await db.SortedSetRangeByScoreAsync(
            sortedSetKey,
            start: minScore,
            stop: maxScore,
            exclude: Exclude.Start,
            order: Order.Ascending,
            skip: 0,
            take: -1);

        foreach (var serializedKey in results)
        {
            try
            {
                if (!TryDeserializeKey(serializedKey, out var key, out _))
                {
                    continue;
                }

                if (key == null)
                {
                    continue;
                }

                var value = await GetValueAsync(key);
                if (value != null)
                {
                    batch.Add(new Entry<TKey, TValue>(key, value));
                }
            }
            catch
            {
                continue;
            }
        }
    }

    private async Task ProcessBatchAsync_Legacy(DateTime now, List<IEntry<TKey, TValue>> batch, IDatabase db)
    {
        var timestamps = await GetAllTimestampsFromRedisAsync();
        var lastBatchKey = $"{GetTimestampsKey()}:last-batch";
        var lastBatchTime = await db.StringGetAsync(lastBatchKey);
        DateTime lastBatchProcessed = DateTime.MinValue;

        if (lastBatchTime.HasValue && long.TryParse(lastBatchTime!, out var ticks))
        {
            lastBatchProcessed = new DateTime(ticks, DateTimeKind.Utc);
        }

        foreach (var kvp in timestamps)
        {
            if (kvp.Value > lastBatchProcessed && now - kvp.Value >= _batchWaitTime)
            {
                var value = await GetValueAsync(kvp.Key);
                if (value != null)
                {
                    batch.Add(new Entry<TKey, TValue>(kvp.Key, value));
                }
            }
        }
    }

    private async Task<Dictionary<TKey, DateTime>> GetAllTimestampsFromRedisAsync()
    {
        var db = _redis.GetDatabase(_database);
        var timestampsKey = GetTimestampsKey();
        var entries = await db.HashGetAllAsync(timestampsKey);

        var result = new Dictionary<TKey, DateTime>();
        foreach (var entry in entries)
        {
            try
            {
                if (!TryDeserializeKey(entry.Name, out var key, out _))
                {
                    continue;
                }

                if (key != null && long.TryParse(entry.Value!, out var ticks))
                {
                    result[key] = new DateTime(ticks, DateTimeKind.Utc);
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
