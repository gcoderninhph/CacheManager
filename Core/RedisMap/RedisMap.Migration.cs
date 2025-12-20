using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace CacheManager.Core;

internal sealed partial class RedisMap<TKey, TValue>
{
    public async Task MigrateTimestampsToSortedSetAsync()
    {
        var db = _redis.GetDatabase(_database);
        var hashKey = GetTimestampsKey();
        var sortedSetKey = GetTimestampsSortedSetKey();

        var sortedSetExists = await db.KeyExistsAsync(sortedSetKey);
        if (sortedSetExists)
        {
            var count = await db.SortedSetLengthAsync(sortedSetKey);
            Console.WriteLine($"[MIGRATION] Sorted Set already exists with {count} entries. Skipping migration.");
            return;
        }

        Console.WriteLine($"[MIGRATION] Starting migration for map: {_mapName}");

        var hashEntries = await db.HashGetAllAsync(hashKey);
        Console.WriteLine($"[MIGRATION] Found {hashEntries.Length} timestamps in Hash");

        if (hashEntries.Length == 0)
        {
            Console.WriteLine($"[MIGRATION] No data to migrate for map: {_mapName}");
            return;
        }

        var sortedSetEntries = new List<SortedSetEntry>();
        int validCount = 0;
        int invalidCount = 0;

        foreach (var entry in hashEntries)
        {
            try
            {
                var serializedKey = entry.Name.ToString();
                var timestampTicks = (long)entry.Value;

                sortedSetEntries.Add(new SortedSetEntry(serializedKey, timestampTicks));
                validCount++;
            }
            catch
            {
                invalidCount++;
            }
        }

        if (sortedSetEntries.Count > 0)
        {
            await db.SortedSetAddAsync(sortedSetKey, sortedSetEntries.ToArray());
            Console.WriteLine($"[MIGRATION] Migrated {validCount} entries to Sorted Set");
        }

        if (invalidCount > 0)
        {
            Console.WriteLine($"[MIGRATION] Skipped {invalidCount} invalid entries");
        }

        var sortedSetCount = await db.SortedSetLengthAsync(sortedSetKey);
        Console.WriteLine($"[MIGRATION] Verification: Sorted Set now has {sortedSetCount} entries");
        Console.WriteLine($"[MIGRATION] Migration complete for map: {_mapName}");
    }

    public async Task<MigrationStatus> GetMigrationStatusAsync()
    {
        var db = _redis.GetDatabase(_database);
        var hashKey = GetTimestampsKey();
        var sortedSetKey = GetTimestampsSortedSetKey();

        var hashCount = await db.HashLengthAsync(hashKey);
        var sortedSetCount = await db.SortedSetLengthAsync(sortedSetKey);

        return new MigrationStatus
        {
            MapName = _mapName,
            HashCount = hashCount,
            SortedSetCount = sortedSetCount,
            IsMigrated = sortedSetCount > 0,
            IsComplete = sortedSetCount >= hashCount
        };
    }

    private sealed class MapEntry
    {
        public required TKey Key { get; init; }
        public required TValue Value { get; init; }
        public Guid Version { get; init; }
        public DateTime LastUpdated { get; init; }
    }
}
