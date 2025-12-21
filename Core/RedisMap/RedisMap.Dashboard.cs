using StackExchange.Redis;

namespace CacheManager.Core;

internal sealed partial class RedisMap<TKey, TValue>
{
    public async Task<IEnumerable<MapEntryData>> GetAllEntriesForDashboardAsync()
    {
        var db = _redis.GetDatabase(_database);
        var hashKey = GetHashKey();
        var serializedKeys = await db.HashKeysAsync(hashKey);

        var result = new List<MapEntryData>();
        foreach (var serializedKey in serializedKeys)
        {
            var entryData = await BuildMapEntryDataAsync(db, hashKey, serializedKey);
            if (entryData != null)
            {
                result.Add(entryData);
            }
        }

        return result;
    }

    public async Task<PagedMapEntries> GetEntriesPagedAsync(int page = 1, int pageSize = 20, string? searchPattern = null)
    {
        var db = _redis.GetDatabase(_database);
        var hashKey = GetHashKey();

        if (!string.IsNullOrWhiteSpace(searchPattern))
        {
            return await GetEntriesWithSearchAsync(page, pageSize, searchPattern);
        }

        var skip = Math.Max(page - 1, 0) * pageSize;
        var totalCount = (int)await db.HashLengthAsync(hashKey);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var result = new List<MapEntryData>();
        var scanned = 0;
        var taken = 0;

        await foreach (var entry in db.HashScanAsync(hashKey, pattern: "*", pageSize: 100))
        {
            if (scanned < skip)
            {
                scanned++;
                continue;
            }

            if (taken >= pageSize)
            {
                break;
            }

            var entryData = await BuildMapEntryDataAsync(db, hashKey, entry.Name);
            if (entryData != null)
            {
                result.Add(entryData);
                taken++;
            }

            scanned++;
        }

        return new PagedMapEntries
        {
            Entries = result,
            CurrentPage = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            HasNext = page < totalPages,
            HasPrev = page > 1
        };
    }

    private async Task<PagedMapEntries> GetEntriesWithSearchAsync(int page, int pageSize, string searchPattern)
    {
        var db = _redis.GetDatabase(_database);
        var hashKey = GetHashKey();
        var matchedEntries = new List<MapEntryData>();

        bool KeyMatches(string key) => key.Contains(searchPattern, StringComparison.OrdinalIgnoreCase);

        await foreach (var entry in db.HashScanAsync(hashKey, pattern: "*", pageSize: 1000))
        {
            var entryData = await BuildMapEntryDataAsync(db, hashKey, entry.Name, KeyMatches);
            if (entryData != null)
            {
                matchedEntries.Add(entryData);
            }
        }

        var totalCount = matchedEntries.Count;
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        var pagedEntries = matchedEntries
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PagedMapEntries
        {
            Entries = pagedEntries,
            CurrentPage = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            HasNext = page < totalPages,
            HasPrev = page > 1
        };
    }

    private async Task<MapEntryData?> BuildMapEntryDataAsync(IDatabase db, RedisKey hashKey, RedisValue rawKey, Func<string, bool>? keyFilter = null)
    {
        TValue? value = default;

        try
        {
            if (!TryDeserializeKey(rawKey, out var key, out var keyString))
            {
                return null;
            }

            if (keyFilter != null && !keyFilter(keyString))
            {
                return null;
            }

            var (found, deserializedValue) = await TryReadValueWithLeaseAsync(db, hashKey, rawKey);
            if (!found || deserializedValue == null)
            {
                return null;
            }

            value = deserializedValue;

            if (value == null || key == null)
            {
                return null;
            }

            var version = await GetVersionFromRedisAsync(key);
            var timestamp = await GetTimestampFromRedisAsync(key);

            return new MapEntryData
            {
                Key = keyString,
                Value = FormatValueForDisplay(value),
                Version = GetShortVersion(version),
                LastModified = FormatTimeAgo(timestamp),
                LastModifiedTicks = timestamp.Ticks
            };
        }
        catch
        {
            return null;
        }
    }

    private static string GetShortVersion(Guid version)
    {
        var compact = version.ToString("N");
        return compact.Length >= 8 ? compact.Substring(0, 8) : compact;
    }
}
