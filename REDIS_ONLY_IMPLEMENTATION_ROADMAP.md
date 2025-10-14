# Redis-Only Implementation Roadmap

## Mục tiêu
Chuyển đổi từ **C# Memory Cache** sang **Redis-Only Storage** để đảm bảo đồng bộ giữa nhiều instances.

---

## Phase 1: Chuẩn bị - Filter Metadata Keys khỏi Dashboard ✅

### Step 1.1: Update CacheStorage.cs - GetAllMapNamesAsync

**File:** `Core/CacheStorage.cs`

**Location:** Tìm method `GetAllMapNamesAsync()`

**Change:**
```csharp
// BEFORE
public async Task<IEnumerable<string>> GetAllMapNamesAsync()
{
    var server = GetServer();
    var pattern = "map:*";
    var keys = server.Keys(_database, pattern);
    
    return keys
        .Select(k => k.ToString().Substring(4)) // Remove "map:" prefix
        .Distinct()
        .OrderBy(n => n);
}

// AFTER
public async Task<IEnumerable<string>> GetAllMapNamesAsync()
{
    var server = GetServer();
    var pattern = "map:*";
    var keys = server.Keys(_database, pattern);
    
    return keys
        .Select(k => k.ToString().Substring(4)) // Remove "map:" prefix
        .Where(name => !name.Contains(":__meta:")) // ✅ Filter out internal metadata
        .Distinct()
        .OrderBy(n => n);
}
```

**Test:**
```bash
# Before: Dashboard shows all Redis keys
GET http://localhost:5011/dashboard
# Shows: products, users, products:__meta:versions, etc.

# After: Dashboard only shows business maps
GET http://localhost:5011/dashboard
# Shows: products, users (no __meta keys)
```

---

## Phase 2: Thêm Redis Helper Methods vào RedisMap.cs

### Step 2.1: Thêm helper methods ở cuối file (trước closing brace của class)

**File:** `Core/RedisMap.cs`

**Location:** Tìm dòng `private string GetHashKey()` (khoảng line 771)

**Add these methods RIGHT AFTER the existing helper methods:**

```csharp
// ==================== REDIS METADATA HELPER METHODS ====================
// All metadata now stored in Redis for multi-instance synchronization

/// <summary>
/// Redis Keys Structure:
/// - map:{mapName}:__meta:versions    → Hash: Version tracking (Guid)
/// - map:{mapName}:__meta:timestamps  → Hash: Last modified timestamps
/// - map:{mapName}:__meta:ttl-config  → String: TTL configuration (seconds)
/// Note: Access time already uses map:{mapName}:access-time (no __meta prefix for backward compatibility)
/// </summary>

private string GetVersionsKey() => $"map:{_mapName}:__meta:versions";

private string GetTimestampsKey() => $"map:{_mapName}:__meta:timestamps";

private string GetTtlConfigKey() => $"map:{_mapName}:__meta:ttl-config";

/// <summary>
/// Get version for a key from Redis
/// </summary>
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
	
	// Generate new version if not exists
	var newVersion = Guid.NewGuid();
	await db.HashSetAsync(versionsKey, fieldName, newVersion.ToString());
	return newVersion;
}

/// <summary>
/// Set version for a key in Redis
/// </summary>
private async Task SetVersionInRedisAsync(TKey key, Guid version)
{
	var db = _redis.GetDatabase(_database);
	var versionsKey = GetVersionsKey();
	var fieldName = SerializeKey(key);
	await db.HashSetAsync(versionsKey, fieldName, version.ToString());
}

/// <summary>
/// Get last modified timestamp for a key from Redis
/// </summary>
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

/// <summary>
/// Set last modified timestamp for a key in Redis
/// </summary>
private async Task SetTimestampInRedisAsync(TKey key, DateTime timestamp)
{
	var db = _redis.GetDatabase(_database);
	var timestampsKey = GetTimestampsKey();
	var fieldName = SerializeKey(key);
	await db.HashSetAsync(timestampsKey, fieldName, timestamp.Ticks);
}

/// <summary>
/// Get TTL configuration from Redis (in seconds)
/// </summary>
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

/// <summary>
/// Set TTL configuration in Redis (in seconds)
/// </summary>
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

/// <summary>
/// Remove version metadata for a key from Redis
/// </summary>
private async Task RemoveVersionFromRedisAsync(TKey key)
{
	var db = _redis.GetDatabase(_database);
	var fieldName = SerializeKey(key);
	
	// Remove from versions hash
	await db.HashDeleteAsync(GetVersionsKey(), fieldName);
	
	// Remove from timestamps hash
	await db.HashDeleteAsync(GetTimestampsKey(), fieldName);
}

/// <summary>
/// Clear all version metadata from Redis
/// </summary>
private async Task ClearAllVersionMetadataAsync()
{
	var db = _redis.GetDatabase(_database);
	
	// Delete all metadata keys
	await db.KeyDeleteAsync(GetVersionsKey());
	await db.KeyDeleteAsync(GetTimestampsKey());
}

/// <summary>
/// Get all versions from Redis (for cleanup/maintenance)
/// </summary>
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
			var key = JsonSerializer.Deserialize<TKey>(entry.Name.ToString(), JsonOptions);
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
```

**Test:**
```bash
dotnet build
# Should compile successfully with new methods added
```

---

## Phase 3: Replace _versionCache với Redis Operations

### Step 3.1: Update SetValueAsync method

**File:** `Core/RedisMap.cs`

**Location:** Find `public async Task SetValueAsync(TKey key, TValue value)` (around line 81)

**Find this code:**
```csharp
// Update version cache
var entry = new MapEntry
{
	Key = key,
	Value = value,
	Version = Guid.NewGuid(),
	LastUpdated = DateTime.UtcNow
};
_versionCache.AddOrUpdate(key, entry, (_, _) => entry);
```

**Replace with:**
```csharp
// ✅ Update version and timestamp in Redis (not C# memory)
var newVersion = Guid.NewGuid();
var now = DateTime.UtcNow;

await SetVersionInRedisAsync(key, newVersion);
await SetTimestampInRedisAsync(key, now);
```

### Step 3.2: Update GetAllEntriesForDashboardAsync method

**Location:** Find `public async Task<IEnumerable<MapEntryData>> GetAllEntriesForDashboardAsync()` (around line 199)

**Find this code:**
```csharp
var version = _versionCache.TryGetValue(key, out var cached) 
	? cached.Version.ToString() 
	: Guid.NewGuid().ToString();
```

**Replace with:**
```csharp
// ✅ Get version from Redis (not C# cache)
var version = await GetVersionFromRedisAsync(key);
```

**And update the result.Add line:**
```csharp
result.Add(new MapEntryData
{
	Key = key.ToString() ?? "",
	Value = SerializeValue(value!),
	Version = version.ToString() // ← version is now Guid, call ToString()
});
```

### Step 3.3: Update GetAllEntriesAsync(Action) method

**Location:** Find the streaming version around line 294

**Find this code:**
```csharp
var version = _versionCache.TryGetValue(key, out var cached)
	? cached.Version.ToString()
	: Guid.NewGuid().ToString();
```

**Replace with:**
```csharp
// ✅ Get version from Redis
var version = await GetVersionFromRedisAsync(key);
```

**And update the Entry creation:**
```csharp
var entry = new Entry<TKey, TValue>(key, value, version.ToString()); // ← Add ToString()
```

### Step 3.4: Update GetAllEntriesAsync() load-all method

**Location:** Find around line 359

**Find similar version cache code:**
```csharp
var version = _versionCache.TryGetValue(key, out var cached)
	? cached.Version.ToString()
	: Guid.NewGuid().ToString();
```

**Replace with:**
```csharp
// ✅ Get version from Redis
var version = await GetVersionFromRedisAsync(key);
```

**Update Entry creation:**
```csharp
result.Add(new Entry<TKey, TValue>(key, value, version.ToString())); // ← Add ToString()
```

### Step 3.5: Update RemoveAsync method

**Location:** Find `public async Task<bool> RemoveAsync(TKey key)` (around line 589)

**Find this code:**
```csharp
_versionCache.TryRemove(key, out _);
```

**Replace with:**
```csharp
// ✅ Remove version from Redis (not C# cache)
await RemoveVersionFromRedisAsync(key);
```

### Step 3.6: Update ClearAsync method

**Location:** Find `public async Task ClearAsync()` (around line 184)

**Find this code:**
```csharp
_versionCache.Clear();
```

**Replace with:**
```csharp
// ✅ Clear all version metadata from Redis
await ClearAllVersionMetadataAsync();
```

### Step 3.7: Update ProcessBatch method

**Location:** Find `private void ProcessBatch(object? state)` (around line 627)

**Find this code:**
```csharp
foreach (var kvp in _versionCache)
{
	var key = kvp.Key;
	
	// Check if key still exists in Redis
	if (!db.HashExists(hashKey, SerializeKey(key)))
	{
		// Remove from version cache if not in Redis
		_versionCache.TryRemove(kvp.Key, out _);
	}
}
```

**Replace with:**
```csharp
// ✅ Get all versions from Redis and cleanup orphaned entries
var versions = await GetAllVersionsFromRedisAsync();

foreach (var kvp in versions)
{
	var key = kvp.Key;
	
	// Check if key still exists in main hash
	if (!await db.HashExistsAsync(hashKey, SerializeKey(key)))
	{
		// Remove orphaned metadata from Redis
		await RemoveVersionFromRedisAsync(key);
	}
}
```

### Step 3.8: Update ProcessExpiration method

**Location:** Find `private void ProcessExpiration(object? state)` (around line 768)

**Find this code:**
```csharp
// Remove from version cache
_versionCache.TryRemove(key, out _);
```

**Replace with:**
```csharp
// ✅ Remove version from Redis
await RemoveVersionFromRedisAsync(key);
```

**Test after each step:**
```bash
dotnet build
# Should compile after each replacement
```

---

## Phase 4: Replace _itemTtl với Redis Operations

### Step 4.1: Update GetValueAsync method

**Location:** Find `public async Task<TValue> GetValueAsync(TKey key)` (around line 60)

**Find this code:**
```csharp
// Update access time nếu có TTL
if (_itemTtl.HasValue)
{
	await UpdateAccessTimeAsync(key);
}
```

**Replace with:**
```csharp
// ✅ Update access time nếu có TTL (loaded from Redis)
var ttl = await GetItemTtlFromRedisAsync();
if (ttl.HasValue)
{
	await UpdateAccessTimeAsync(key);
}
```

### Step 4.2: Update SetValueAsync method

**Location:** Same method as Step 3.1

**Find this code:**
```csharp
// Update access time nếu có TTL
if (_itemTtl.HasValue)
{
	await UpdateAccessTimeAsync(key);
}
```

**Replace with:**
```csharp
// ✅ Update access time nếu có TTL (loaded from Redis)
var ttl = await GetItemTtlFromRedisAsync();
if (ttl.HasValue)
{
	await UpdateAccessTimeAsync(key);
}
```

### Step 4.3: Update SetItemExpiration method

**Location:** Find `public void SetItemExpiration(TimeSpan? ttl)` (around line 159)

**Find this code:**
```csharp
public void SetItemExpiration(TimeSpan? ttl)
{
	_itemTtl = ttl;
	
	if (ttl.HasValue && _expirationTimer == null)
	{
		// Khởi tạo timer để check expiration mỗi giây
		_expirationTimer = new Timer(ProcessExpiration, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
	}
	else if (!ttl.HasValue && _expirationTimer != null)
	{
		// Tắt timer nếu không còn TTL
		_expirationTimer.Dispose();
		_expirationTimer = null;
	}
}
```

**Replace with:**
```csharp
public async Task SetItemExpirationAsync(TimeSpan? ttl)
{
	// ✅ Save to Redis (not C# memory)
	await SetItemTtlInRedisAsync(ttl);
	
	if (ttl.HasValue && _expirationTimer == null)
	{
		// Khởi tạo timer để check expiration mỗi giây
		_expirationTimer = new Timer(ProcessExpiration, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
	}
	else if (!ttl.HasValue && _expirationTimer != null)
	{
		// Tắt timer nếu không còn TTL
		_expirationTimer.Dispose();
		_expirationTimer = null;
	}
}
```

**⚠️ IMPORTANT: This method signature changed from `void` to `async Task`**

### Step 4.4: Update Map.cs interface

**File:** `Core/Map.cs`

**Find this line:**
```csharp
void SetItemExpiration(TimeSpan? ttl);
```

**Replace with:**
```csharp
Task SetItemExpirationAsync(TimeSpan? ttl);
```

### Step 4.5: Update all callers of SetItemExpiration

**Files to update:**
- `Asp.Net.Test/Controllers/TtlTestController.cs`
- Any other controllers using SetItemExpiration

**Find:**
```csharp
map.SetItemExpiration(TimeSpan.FromMinutes(5));
```

**Replace with:**
```csharp
await map.SetItemExpirationAsync(TimeSpan.FromMinutes(5));
```

### Step 4.6: Update ClearAsync method (TTL cleanup)

**Location:** Find `public async Task ClearAsync()` 

**Find this code:**
```csharp
// Xóa luôn sorted set tracking access time
if (_itemTtl.HasValue)
{
	var accessTimeKey = GetAccessTimeKey();
	await db.KeyDeleteAsync(accessTimeKey);
}
```

**Replace with:**
```csharp
// ✅ Xóa luôn sorted set tracking access time (check TTL from Redis)
var ttl = await GetItemTtlFromRedisAsync();
if (ttl.HasValue)
{
	var accessTimeKey = GetAccessTimeKey();
	await db.KeyDeleteAsync(accessTimeKey);
}

// ✅ Also delete TTL config
await db.KeyDeleteAsync(GetTtlConfigKey());
```

### Step 4.7: Update ProcessExpiration method

**Location:** Find `private void ProcessExpiration(object? state)`

**Find this code:**
```csharp
if (!_itemTtl.HasValue)
{
	return;
}

// ... later in method ...

var expirationThreshold = now - (long)_itemTtl.Value.TotalSeconds;
```

**Replace with:**
```csharp
// ✅ Load TTL from Redis each time
var ttl = GetItemTtlFromRedisAsync().Result;
if (!ttl.HasValue)
{
	return;
}

// ... later in method ...

var expirationThreshold = now - (long)ttl.Value.TotalSeconds;
```

**Test:**
```bash
dotnet build
# Should compile successfully
```

---

## Phase 5: Remove C# Memory Cache Fields

### Step 5.1: Comment out (don't delete yet) memory cache fields

**Location:** Top of RedisMap class (around line 17)

**Find:**
```csharp
private readonly ConcurrentDictionary<TKey, MapEntry> _versionCache;
private TimeSpan? _itemTtl = null;
```

**Comment out:**
```csharp
// ❌ REMOVED: Now using Redis for version storage
// private readonly ConcurrentDictionary<TKey, MapEntry> _versionCache;

// ❌ REMOVED: Now using Redis for TTL config storage
// private TimeSpan? _itemTtl = null;
```

### Step 5.2: Comment out initialization in constructor

**Location:** Constructor (around line 38)

**Find:**
```csharp
_versionCache = new ConcurrentDictionary<TKey, MapEntry>();
```

**Comment out:**
```csharp
// ❌ REMOVED: _versionCache = new ConcurrentDictionary<TKey, MapEntry>();
```

### Step 5.3: Add initialization for expiration timer from Redis

**Location:** End of constructor

**Add this code:**
```csharp
// ✅ Load TTL config from Redis and start expiration timer if needed
_ = InitializeExpirationTimerAsync();
```

**Then add this new method after constructor:**
```csharp
/// <summary>
/// Initialize expiration timer by loading TTL config from Redis
/// </summary>
private async Task InitializeExpirationTimerAsync()
{
	try
	{
		var ttl = await GetItemTtlFromRedisAsync();
		if (ttl.HasValue && _expirationTimer == null)
		{
			_expirationTimer = new Timer(ProcessExpiration, null, 
				TimeSpan.FromSeconds(1), 
				TimeSpan.FromSeconds(1));
		}
	}
	catch
	{
		// Ignore initialization errors
	}
}
```

**Test:**
```bash
dotnet build
# Should compile successfully with all changes
```

---

## Phase 6: Testing

### Test 6.1: Basic Functionality
```bash
# Start application
.\run_aspnet.cmd

# Test Set/Get
curl -X POST "http://localhost:5011/api/test/set?key=test1&value=hello"
curl "http://localhost:5011/api/test/get?key=test1"

# Check Redis
redis-cli
> HGET map:products "1"
> HGET map:products:__meta:versions "1"
> HGET map:products:__meta:timestamps "1"
```

### Test 6.2: Dashboard Filter
```bash
# Check dashboard
curl "http://localhost:5011/dashboard"

# Should NOT show:
# - products:__meta:versions
# - products:__meta:timestamps
# - products:__meta:ttl-config

# Should show:
# - products ✅
# - users ✅
```

### Test 6.3: TTL Functionality
```bash
# Set TTL
curl -X POST "http://localhost:5011/api/ttl/set-ttl?mapName=products&ttlMinutes=1"

# Check Redis
redis-cli
> GET map:products:__meta:ttl-config
# Should return "60" (seconds)

# Wait 1 minute, keys should expire
```

### Test 6.4: Multi-Instance Test
```bash
# Terminal 1
cd Asp.Net.Test
dotnet run --urls "http://localhost:5011"

# Terminal 2
cd Asp.Net.Test
dotnet run --urls "http://localhost:5012"

# Set on instance 1
curl -X POST "http://localhost:5011/api/test/set?key=multi1&value=test"

# Get from instance 2 (should work)
curl "http://localhost:5012/api/test/get?key=multi1"

# Check version consistency
curl "http://localhost:5011/api/map/entries?mapName=products" | jq '.entries[] | select(.key=="multi1")'
curl "http://localhost:5012/api/map/entries?mapName=products" | jq '.entries[] | select(.key=="multi1")'
# Versions should match ✅
```

---

## Phase 7: Cleanup & Commit

### Step 7.1: Remove commented code
Once everything works, remove all commented `_versionCache` and `_itemTtl` lines.

### Step 7.2: Commit changes
```bash
git add .
git commit -m "feat: Migrate to Redis-only storage for multi-instance support

- Removed C# memory cache (_versionCache, _itemTtl)
- Added Redis metadata storage with :__meta: prefix
- Filtered internal metadata keys from dashboard
- All metadata now persistent in Redis
- Multi-instance synchronization enabled
- TTL config shared across instances

Redis Keys:
- map:{name}:__meta:versions (Hash: version tracking)
- map:{name}:__meta:timestamps (Hash: timestamps)
- map:{name}:__meta:ttl-config (String: TTL seconds)

Benefits:
✅ Multi-instance safe
✅ Full persistence
✅ Clean dashboard
✅ Consistent state"
```

---

## Troubleshooting

### Issue 1: Build errors after Phase 3
**Solution:** Make sure you added all helper methods from Phase 2 first.

### Issue 2: Dashboard still shows __meta keys
**Solution:** Check CacheStorage.cs filter is applied correctly (Phase 1).

### Issue 3: TTL not working after restart
**Solution:** Check InitializeExpirationTimerAsync is called in constructor (Phase 5.3).

### Issue 4: Version mismatch between instances
**Solution:** Make sure all instances use same Redis database number.

### Issue 5: Performance degradation
**Solution:** Consider adding optional local caching layer (see REDIS_ONLY_MIGRATION_GUIDE.md).

---

## Performance Monitoring

After migration, monitor these metrics:

```csharp
// Add performance logging
var sw = Stopwatch.StartNew();
var version = await GetVersionFromRedisAsync(key);
sw.Stop();
if (sw.ElapsedMilliseconds > 50)
{
	_logger.LogWarning("Slow Redis version fetch: {Ms}ms for key {Key}", 
		sw.ElapsedMilliseconds, key);
}
```

**Expected performance:**
- Version fetch: < 5ms
- Version set: < 5ms
- TTL config fetch: < 2ms
- Acceptable for most scenarios ✅

---

## Next Steps

After successful migration:

1. **Optional Local Caching**: Add MemoryCache with short TTL for read-heavy scenarios
2. **Redis Pub/Sub**: Implement cache invalidation notifications
3. **Monitoring**: Add metrics tracking for Redis operations
4. **Load Testing**: Test with high concurrency and multiple instances
5. **Documentation**: Update API documentation with new behavior

---

## Summary

✅ **Phase 1**: Dashboard filter (15 min)
✅ **Phase 2**: Add Redis helpers (30 min)
✅ **Phase 3**: Replace _versionCache (45 min)
✅ **Phase 4**: Replace _itemTtl (45 min)
✅ **Phase 5**: Remove memory fields (15 min)
✅ **Phase 6**: Testing (1 hour)
✅ **Phase 7**: Cleanup & commit (15 min)

**Total time: ~3-4 hours**

**Result:**
- ✅ Multi-instance synchronization
- ✅ Full Redis persistence
- ✅ Clean dashboard
- ✅ Zero data loss on restart
- ✅ Production-ready
