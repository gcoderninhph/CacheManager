# Redis-Only Cache Storage Migration Guide

## Tổng quan thay đổi

Chuyển đổi từ **C# Memory Cache** sang **Redis-Only Storage** để đảm bảo đồng bộ giữa nhiều instances.

## Cấu trúc Redis Keys

### 1. Main Data Storage
```redis
Key: map:{mapName}
Type: Hash
Purpose: Lưu trữ dữ liệu chính (key-value pairs)
```

### 2. Internal Metadata Keys (Hidden from Dashboard)
```redis
# Version tracking
Key: map:{mapName}:__meta:versions
Type: Hash
Field: serialized key
Value: GUID string

# Timestamp tracking  
Key: map:{mapName}:__meta:timestamps
Type: Hash
Field: serialized key
Value: DateTime.Ticks (long)

# TTL configuration
Key: map:{mapName}:__meta:ttl-config
Type: String
Value: TTL duration in seconds (double)

# Access time tracking (for TTL expiration)
Key: map:{mapName}:__meta:access-time
Type: Sorted Set
Member: serialized key
Score: Unix timestamp (seconds)
```

## Lọc Maps trên Dashboard

Maps có prefix `:__meta:` sẽ được ẩn khỏi dashboard.

### Filter Logic trong CacheStorage.cs

```csharp
public async Task<IEnumerable<string>> GetAllMapNamesAsync()
{
    var server = GetServer();
    var pattern = "map:*";
    var keys = server.Keys(_database, pattern);
    
    return keys
        .Select(k => k.ToString().Substring(4)) // Remove "map:" prefix
        .Where(name => !name.Contains(":__meta:")) // ✅ Filter out metadata keys
        .Distinct()
        .OrderBy(n => n);
}
```

## Thay đổi trong RedisMap.cs

### ❌ Removed (C# Memory Cache)
```csharp
// BEFORE
private readonly ConcurrentDictionary<TKey, MapEntry> _versionCache;
private TimeSpan? _itemTtl = null;

// Constructor
_versionCache = new ConcurrentDictionary<TKey, MapEntry>();

// Usage
_versionCache.AddOrUpdate(key, entry, (_, _) => entry);
var version = _versionCache.TryGetValue(key, out var cached) ? cached.Version : Guid.NewGuid();
_versionCache.TryRemove(key, out _);
_versionCache.Clear();
```

### ✅ Added (Redis Storage)
```csharp
// Helper methods
private async Task<Guid> GetVersionFromRedisAsync(TKey key);
private async Task SetVersionInRedisAsync(TKey key, Guid version);
private async Task<DateTime> GetTimestampFromRedisAsync(TKey key);
private async Task SetTimestampInRedisAsync(TKey key, DateTime timestamp);
private async Task<TimeSpan?> GetItemTtlFromRedisAsync();
private async Task SetItemTtlInRedisAsync(TimeSpan? ttl);
private async Task RemoveVersionFromRedisAsync(TKey key);
private async Task ClearAllVersionMetadataAsync();

// Usage
var version = await GetVersionFromRedisAsync(key);
await SetVersionInRedisAsync(key, Guid.NewGuid());
var ttl = await GetItemTtlFromRedisAsync();
```

## Implementation Details

### 1. SetValueAsync - Update version in Redis
```csharp
public async Task SetValueAsync(TKey key, TValue value)
{
    // ... set value in hash ...
    
    // ✅ Update version and timestamp in Redis
    var newVersion = Guid.NewGuid();
    var now = DateTime.UtcNow;
    
    await SetVersionInRedisAsync(key, newVersion);
    await SetTimestampInRedisAsync(key, now);
    
    // Check TTL from Redis
    var ttl = await GetItemTtlFromRedisAsync();
    if (ttl.HasValue)
    {
        await UpdateAccessTimeAsync(key);
    }
}
```

### 2. SetItemExpiration - Store TTL in Redis
```csharp
public async Task SetItemExpiration(TimeSpan? ttl)
{
    // ✅ Save to Redis (not C# memory)
    await SetItemTtlInRedisAsync(ttl);
    
    if (ttl.HasValue && _expirationTimer == null)
    {
        _expirationTimer = new Timer(ProcessExpiration, null, 
            TimeSpan.FromSeconds(1), 
            TimeSpan.FromSeconds(1));
    }
    else if (!ttl.HasValue && _expirationTimer != null)
    {
        _expirationTimer.Dispose();
        _expirationTimer = null;
    }
}
```

### 3. GetAllEntriesForDashboardAsync - Load version from Redis
```csharp
public async Task<IEnumerable<MapEntryData>> GetAllEntriesForDashboardAsync()
{
    var entries = await db.HashGetAllAsync(hashKey);
    
    foreach (var entry in entries)
    {
        var key = JsonSerializer.Deserialize<TKey>(entry.Name.ToString());
        
        // ✅ Get version from Redis (not C# cache)
        var version = await GetVersionFromRedisAsync(key);
        
        result.Add(new MapEntryData {
            Key = key.ToString(),
            Value = SerializeValue(value),
            Version = version.ToString()
        });
    }
}
```

### 4. ProcessExpiration - Load TTL from Redis
```csharp
private void ProcessExpiration(object? state)
{
    // ✅ Load TTL from Redis each time
    var ttl = GetItemTtlFromRedisAsync().Result;
    if (!ttl.HasValue) return;
    
    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var expirationThreshold = now - (long)ttl.Value.TotalSeconds;
    
    // ... check and remove expired keys ...
    
    // ✅ Remove version from Redis (not C# cache)
    await RemoveVersionFromRedisAsync(key);
}
```

### 5. ProcessBatch - Load versions from Redis
```csharp
private void ProcessBatch(object? state)
{
    // ✅ Get all versions from Redis
    var versions = GetAllVersionsFromRedisAsync().Result;
    
    foreach (var kvp in versions)
    {
        // Check if still exists in main hash
        if (!db.HashExists(hashKey, SerializeKey(kvp.Key)))
        {
            // Remove orphaned metadata
            RemoveVersionFromRedisAsync(kvp.Key).Wait();
        }
    }
}
```

## Benefits

### 1. ✅ Multi-Instance Synchronization
- Nhiều instances C# có thể chạy đồng thời
- Version tracking consistent across instances
- TTL config shared giữa tất cả instances

### 2. ✅ Persistence
- Restart C# app không mất version data
- Restart C# app không mất TTL config
- All metadata persistent in Redis

### 3. ✅ Clean Dashboard
- Metadata keys ẩn khỏi dashboard
- Chỉ hiển thị business maps
- Naming convention rõ ràng (`:__meta:`)

### 4. ✅ Atomic Operations
- Redis operations atomic
- No race conditions between instances
- Consistent state

## Performance Considerations

### Potential Overhead
```csharp
// BEFORE (C# Memory): O(1) in-memory lookup
var version = _versionCache.TryGetValue(key, out var cached) ? cached.Version : Guid.NewGuid();

// AFTER (Redis): O(1) Redis HGET
var version = await GetVersionFromRedisAsync(key);
```

**Network Round-Trips:**
- Each operation requires Redis call
- Can batch operations where possible
- Consider caching for read-heavy scenarios

### Optimization Strategies

#### 1. Batch Operations
```csharp
// Get multiple versions at once
public async Task<Dictionary<TKey, Guid>> GetVersionsAsync(IEnumerable<TKey> keys)
{
    var db = _redis.GetDatabase(_database);
    var versionsKey = GetVersionsKey();
    var fieldNames = keys.Select(SerializeKey).ToArray();
    var values = await db.HashGetAsync(versionsKey, fieldNames);
    // ... deserialize ...
}
```

#### 2. Pipeline Operations
```csharp
// Set value + version + timestamp in one batch
var batch = db.CreateBatch();
var task1 = batch.HashSetAsync(hashKey, fieldName, serializedValue);
var task2 = batch.HashSetAsync(GetVersionsKey(), fieldName, newVersion.ToString());
var task3 = batch.HashSetAsync(GetTimestampsKey(), fieldName, now.Ticks);
batch.Execute();
await Task.WhenAll(task1, task2, task3);
```

#### 3. Local Caching (Optional)
```csharp
// Add optional local cache with short TTL for read-heavy scenarios
private readonly MemoryCache _localVersionCache = new MemoryCache(new MemoryCacheOptions {
    ExpirationScanFrequency = TimeSpan.FromSeconds(10)
});

private async Task<Guid> GetVersionFromRedisAsync(TKey key)
{
    var cacheKey = $"version:{_mapName}:{SerializeKey(key)}";
    
    if (_localVersionCache.TryGetValue(cacheKey, out Guid cachedVersion))
    {
        return cachedVersion;
    }
    
    var version = await GetVersionFromRedisDirectAsync(key);
    
    _localVersionCache.Set(cacheKey, version, TimeSpan.FromSeconds(5));
    
    return version;
}
```

## Migration Steps

### 1. Update CacheStorage.cs
```csharp
// Filter out metadata keys from dashboard
public async Task<IEnumerable<string>> GetAllMapNamesAsync()
{
    return keys
        .Select(k => k.ToString().Substring(4))
        .Where(name => !name.Contains(":__meta:"))  // ← Add this
        .Distinct();
}
```

### 2. Update RedisMap.cs
- Remove `_versionCache` field
- Remove `_itemTtl` field
- Add Redis helper methods
- Replace all `_versionCache` usage with Redis operations
- Replace all `_itemTtl` usage with Redis operations

### 3. Test Multi-Instance Scenario
```bash
# Terminal 1
cd Asp.Net.Test
dotnet run --urls "http://localhost:5011"

# Terminal 2
cd Asp.Net.Test
dotnet run --urls "http://localhost:5012"

# Test version consistency
curl http://localhost:5011/api/test/set?key=1&value=test1
curl http://localhost:5012/api/test/get?key=1
# Should see consistent version
```

### 4. Verify Dashboard
```
GET http://localhost:5011/dashboard

# Should NOT show:
- map:products:__meta:versions
- map:products:__meta:timestamps
- map:products:__meta:ttl-config
- map:products:__meta:access-time

# Should show:
- map:products ✅
- map:users ✅
```

## Testing Checklist

- [ ] Set value on instance 1, get from instance 2
- [ ] Version consistent across instances
- [ ] TTL configuration shared between instances
- [ ] Dashboard hides metadata keys
- [ ] Expiration works correctly
- [ ] Batch updates work
- [ ] Performance acceptable (<50ms per operation)
- [ ] Memory usage reduced (no C# cache)

## Rollback Plan

If needed, can rollback by:
1. Restore `_versionCache` field
2. Restore `_itemTtl` field
3. Load initial data from Redis into memory cache
4. Continue with hybrid approach

## Future Enhancements

### 1. Redis Pub/Sub for Cache Invalidation
```csharp
// Subscribe to version updates
_subscriber.Subscribe($"map:{_mapName}:version-update", (channel, message) => {
    // Invalidate local cache
    _localVersionCache.Remove(message);
});

// Publish on version change
await _subscriber.PublishAsync($"map:{_mapName}:version-update", SerializeKey(key));
```

### 2. Redis Streams for Audit Log
```csharp
// Log all changes to Redis Stream
await db.StreamAddAsync($"map:{_mapName}:__meta:audit-log", new NameValueEntry[] {
    new("action", "set"),
    new("key", SerializeKey(key)),
    new("version", newVersion.ToString()),
    new("timestamp", DateTime.UtcNow.ToString("O"))
});
```

### 3. Distributed Locking
```csharp
// Use RedLock for distributed locking
using (var redLock = await _redLockFactory.CreateLockAsync($"lock:map:{_mapName}:{key}", TimeSpan.FromSeconds(5)))
{
    if (redLock.IsAcquired)
    {
        // Perform atomic operation
    }
}
```

## Conclusion

Migration from C# memory cache to Redis-only storage provides:
- ✅ Multi-instance synchronization
- ✅ Full persistence
- ✅ Clean dashboard (hidden metadata)
- ✅ Consistent state across restarts
- ⚠️ Slight performance overhead (network calls)
- ⚠️ Requires careful testing

**Recommendation**: Proceed with migration + add optional local caching for performance-critical paths.
