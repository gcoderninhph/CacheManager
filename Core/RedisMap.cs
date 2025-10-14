using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace CacheManager.Core;

internal sealed class RedisMap<TKey, TValue> : IMap<TKey, TValue> where TKey : notnull
{
	private readonly IConnectionMultiplexer _redis;
	private readonly string _mapName;
	private readonly int _database;
	
	// ==================== DEPRECATED: C# MEMORY CACHE (Replaced by Redis) ====================
	// These fields are NO LONGER USED - all metadata now stored in Redis for multi-instance sync
	// Kept temporarily for reference, will be removed after full testing
	[Obsolete("Replaced by Redis storage: map:{mapName}:__meta:versions")]
	private readonly ConcurrentDictionary<TKey, MapEntry> _versionCache;
	
	// TTL cached in memory for quick synchronous checks (loaded from Redis on startup)
	// Source of truth is Redis: map:{mapName}:__meta:ttl-config
	private TimeSpan? _itemTtl = null;
	// ======================================================================================
	
	private readonly List<Action<TKey, TValue>> _onAddHandlers;
	private readonly List<Action<TKey, TValue>> _onUpdateHandlers;
	private readonly List<Action<TKey, TValue>> _onRemoveHandlers;
	private readonly List<Action> _onClearHandlers;
	private readonly List<Action<IEnumerable<IEntry<TKey, TValue>>>> _onBatchUpdateHandlers;
	private readonly List<Action<TKey, TValue>> _onExpiredHandlers;
	private readonly Timer? _batchTimer;
	private Timer? _expirationTimer;
	private readonly TimeSpan _batchWaitTime;
	private readonly object _lockObj = new();
	
	// JSON serialization options - mặc định format đẹp, camelCase
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = false, // Compact để tiết kiệm bandwidth
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // camelCase cho properties
		PropertyNameCaseInsensitive = true, // Case-insensitive khi deserialize
		DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
	};

	public RedisMap(
		IConnectionMultiplexer redis,
		string mapName,
		int database = -1,
		TimeSpan? batchWaitTime = null)
	{
		_redis = redis;
		_mapName = mapName;
		_database = database;
		
		// DEPRECATED: C# memory cache no longer used
		#pragma warning disable CS0618 // Type or member is obsolete
		_versionCache = new ConcurrentDictionary<TKey, MapEntry>(); // Keep for MapEntry class reference
		#pragma warning restore CS0618
		
		_onAddHandlers = new List<Action<TKey, TValue>>();
		_onUpdateHandlers = new List<Action<TKey, TValue>>();
		_onRemoveHandlers = new List<Action<TKey, TValue>>();
		_onClearHandlers = new List<Action>();
		_onBatchUpdateHandlers = new List<Action<IEnumerable<IEntry<TKey, TValue>>>>();
		_onExpiredHandlers = new List<Action<TKey, TValue>>();
		_batchWaitTime = batchWaitTime ?? TimeSpan.FromSeconds(5);

		// Load TTL config from Redis on startup (fire and forget, cache will be populated async)
		_ = InitializeTtlFromRedisAsync();

		// Always start batch timer - it will check if there are handlers
		_batchTimer = new Timer(ProcessBatch, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
	}

	/// <summary>
	/// Load TTL configuration from Redis on startup and initialize expiration timer if needed
	/// </summary>
	private async Task InitializeTtlFromRedisAsync()
	{
		try
		{
			var ttl = await GetItemTtlFromRedisAsync();
			if (ttl.HasValue)
			{
				_itemTtl = ttl;
				
				// Start expiration timer if TTL exists
				if (_expirationTimer == null)
				{
					_expirationTimer = new Timer(ProcessExpiration, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
				}
			}
		}
		catch
		{
			// Ignore initialization errors - TTL will be null by default
		}
	}

	public async Task<TValue> GetValueAsync(TKey key)
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		var fieldName = SerializeKey(key);
		var value = await db.HashGetAsync(hashKey, fieldName);

		if (!value.HasValue)
		{
			throw new KeyNotFoundException($"Key '{key}' not found in map '{_mapName}'");
		}

		// Update access time nếu có TTL
		var ttl = await GetItemTtlFromRedisAsync();
		if (ttl.HasValue)
		{
			await UpdateAccessTimeAsync(key);
		}

		return DeserializeValue(value!);
	}

	public async Task SetValueAsync(TKey key, TValue value)
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		var fieldName = SerializeKey(key);
		var serializedValue = SerializeValue(value);

		var existed = await db.HashExistsAsync(hashKey, fieldName);
		await db.HashSetAsync(hashKey, fieldName, serializedValue);

		// Update access time nếu có TTL
		var ttl = await GetItemTtlFromRedisAsync();
		if (ttl.HasValue)
		{
			await UpdateAccessTimeAsync(key);
		}

		// Update version and timestamp in Redis
		var newVersion = Guid.NewGuid();
		var timestamp = DateTime.UtcNow;
		await SetVersionInRedisAsync(key, newVersion);
		await SetTimestampInRedisAsync(key, timestamp);

		// Trigger events
		if (existed)
		{
			TriggerUpdateHandlers(key, value);
		}
		else
		{
			TriggerAddHandlers(key, value);
		}
	}

	public void OnAdd(Action<TKey, TValue> addAction)
	{
		lock (_lockObj)
		{
			_onAddHandlers.Add(addAction);
		}
	}

	public void OnUpdate(Action<TKey, TValue> updateAction)
	{
		lock (_lockObj)
		{
			_onUpdateHandlers.Add(updateAction);
		}
	}

	public void OnRemove(Action<TKey, TValue> removeAction)
	{
		lock (_lockObj)
		{
			_onRemoveHandlers.Add(removeAction);
		}
	}

	public void OnClear(Action clearAction)
	{
		lock (_lockObj)
		{
			_onClearHandlers.Add(clearAction);
		}
	}

	public void OnBatchUpdate(Action<IEnumerable<IEntry<TKey, TValue>>> batchUpdateAction)
	{
		lock (_lockObj)
		{
			_onBatchUpdateHandlers.Add(batchUpdateAction);
		}
	}

	public void SetItemExpiration(TimeSpan? ttl)
	{
		// Store TTL in Redis (fire and forget)
		_ = SetItemTtlInRedisAsync(ttl);
		
		// Also cache in memory for quick synchronous checks (will be loaded from Redis on startup)
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

	public void OnExpired(Action<TKey, TValue> expiredAction)
	{
		lock (_lockObj)
		{
			_onExpiredHandlers.Add(expiredAction);
		}
	}

	public async Task ClearAsync()
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		await db.KeyDeleteAsync(hashKey);
		
		// Xóa luôn sorted set tracking access time
		var ttl = await GetItemTtlFromRedisAsync();
		if (ttl.HasValue)
		{
			var accessTimeKey = GetAccessTimeKey();
			await db.KeyDeleteAsync(accessTimeKey);
		}
		
		// Clear all metadata in Redis
		await ClearAllVersionMetadataAsync();
		
		TriggerClearHandlers();
	}

	/// <summary>
	/// Get all entries for dashboard display (includes version info)
	/// </summary>
	public async Task<IEnumerable<MapEntryData>> GetAllEntriesForDashboardAsync()
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		var entries = await db.HashGetAllAsync(hashKey);

		var result = new List<MapEntryData>();
		foreach (var entry in entries)
		{
			try
			{
				var key = JsonSerializer.Deserialize<TKey>(entry.Name.ToString());
				var value = DeserializeValue(entry.Value!);
				
				if (key != null)
				{
					// Get version from Redis instead of memory cache
					var version = await GetVersionFromRedisAsync(key);
					
					// Get timestamp from Redis
					var timestamp = await GetTimestampFromRedisAsync(key);
					var timeAgo = FormatTimeAgo(timestamp);
					
					result.Add(new MapEntryData
					{
						Key = key.ToString() ?? "",
						Value = SerializeValue(value!), // Serialize to JSON instead of ToString()
						Version = version.ToString().Substring(0, 8), // Short version (first 8 chars)
						LastModified = timeAgo,
						LastModifiedTicks = timestamp.Ticks
					});
				}
			}
			catch
			{
				// Skip invalid entries
			}
		}

		return result;
	}

	/// <summary>
	/// Get entries with server-side pagination using Redis HSCAN
	/// Tối ưu cho hash có hàng triệu records
	/// </summary>
	public async Task<PagedMapEntries> GetEntriesPagedAsync(int page = 1, int pageSize = 20, string? searchPattern = null)
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		
		// Nếu có search, vẫn phải dùng cách filter trên application
		// Vì Redis không support search trong hash keys
		if (!string.IsNullOrWhiteSpace(searchPattern))
		{
			return await GetEntriesWithSearchAsync(page, pageSize, searchPattern);
		}

		// Tính toán số records cần skip
		var skip = (page - 1) * pageSize;
		
		// Get total count - Redis HLEN O(1) complexity - RẤT NHANH!
		var totalCount = (int)await db.HashLengthAsync(hashKey);
		var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

		// Dùng HSCAN để iterate - CHỈ LẤY DATA CẦN THIẾT
		var result = new List<MapEntryData>();
		var scanned = 0;
		var taken = 0;

		// HashScan returns IAsyncEnumerable - iterate trực tiếp
		await foreach (var entry in db.HashScanAsync(hashKey, pattern: "*", pageSize: 100))
		{
			// Skip records trước page hiện tại
			if (scanned < skip)
			{
				scanned++;
				continue;
			}

			// Đã lấy đủ số lượng cần thiết
			if (taken >= pageSize)
			{
				break;
			}

			try
			{
				var key = JsonSerializer.Deserialize<TKey>(entry.Name.ToString());
				var value = DeserializeValue(entry.Value!);
				
				if (key != null)
				{
					// Get version from Redis instead of memory cache
					var version = await GetVersionFromRedisAsync(key);
					
					// Get timestamp from Redis
					var timestamp = await GetTimestampFromRedisAsync(key);
					var timeAgo = FormatTimeAgo(timestamp);
					
					result.Add(new MapEntryData
					{
						Key = key.ToString() ?? "",
						Value = SerializeValue(value!), // Serialize to JSON instead of ToString()
						Version = version.ToString().Substring(0, 8), // Short version
						LastModified = timeAgo,
						LastModifiedTicks = timestamp.Ticks
					});
					
					taken++;
				}
			}
			catch
			{
				// Skip invalid entries
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

	/// <summary>
	/// Get entries with search filter
	/// Lưu ý: Redis không support filter, phải scan và filter trên application
	/// </summary>
	private async Task<PagedMapEntries> GetEntriesWithSearchAsync(int page, int pageSize, string searchPattern)
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		
		// Với search, phải scan tất cả và filter
		// Nhưng vẫn dùng HSCAN thay vì HashGetAllAsync để tiết kiệm memory
		var matchedEntries = new List<MapEntryData>();

		// Scan từng batch thay vì load all
		await foreach (var entry in db.HashScanAsync(hashKey, pattern: "*", pageSize: 1000))
		{
			try
			{
				var key = JsonSerializer.Deserialize<TKey>(entry.Name.ToString());
				var keyString = key?.ToString() ?? "";
				
				// Filter by search pattern
				if (!keyString.Contains(searchPattern, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var value = DeserializeValue(entry.Value!);
				
				if (key != null)
				{
					// Get version from Redis instead of memory cache
					var version = await GetVersionFromRedisAsync(key);
					
					// Get timestamp from Redis
					var timestamp = await GetTimestampFromRedisAsync(key);
					var timeAgo = FormatTimeAgo(timestamp);
					
					matchedEntries.Add(new MapEntryData
					{
						Key = keyString,
						Value = SerializeValue(value!), // Serialize to JSON instead of ToString()
						Version = version.ToString().Substring(0, 8), // Short version
						LastModified = timeAgo,
						LastModifiedTicks = timestamp.Ticks
					});
				}
			}
			catch
			{
				// Skip invalid entries
			}
		}

		// Pagination trên filtered results
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

	#region IMap<TKey, TValue> Implementation - Basic Operations

	public async Task<bool> ContainsKeyAsync(TKey key)
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		var fieldName = SerializeKey(key);
		return await db.HashExistsAsync(hashKey, fieldName);
	}

	public async Task<int> CountAsync()
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		return (int)await db.HashLengthAsync(hashKey);
	}

	public async Task<IEnumerable<TValue>> GetAllValuesAsync()
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		var values = await db.HashValuesAsync(hashKey);
		
		var result = new List<TValue>();
		foreach (var value in values)
		{
			try
			{
				var deserializedValue = DeserializeValue(value!);
				if (deserializedValue != null)
				{
					result.Add(deserializedValue);
				}
			}
			catch
			{
				// Skip invalid entries
			}
		}
		
		return result;
	}

	public async Task<IEnumerable<TKey>> GetAllKeysAsync()
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		var entries = await db.HashGetAllAsync(hashKey);
		
		var result = new List<TKey>();
		foreach (var entry in entries)
		{
			try
			{
				var key = JsonSerializer.Deserialize<TKey>(entry.Name.ToString());
				if (key != null)
				{
					result.Add(key);
				}
			}
			catch
			{
				// Skip invalid entries
			}
		}
		
		return result;
	}

	public async Task<IEnumerable<IEntry<TKey, TValue>>> GetAllEntriesAsync()
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		var entries = await db.HashGetAllAsync(hashKey);
		
		var result = new List<IEntry<TKey, TValue>>();
		foreach (var entry in entries)
		{
			try
			{
				var key = JsonSerializer.Deserialize<TKey>(entry.Name.ToString());
				var value = DeserializeValue(entry.Value!);
				
				if (key != null && value != null)
				{
					result.Add(new Entry<TKey, TValue>(key, value));
				}
			}
			catch
			{
				// Skip invalid entries
			}
		}
		
		return result;
	}

	#endregion

	#region IMap<TKey, TValue> Implementation - Streaming Operations (Memory Optimized)

	/// <summary>
	/// Stream all keys với HSCAN - tối ưu memory cho map có hàng triệu phần tử
	/// Không load toàn bộ keys vào memory cùng lúc
	/// </summary>
	public async Task GetAllKeysAsync(Action<TKey> keyAction)
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		
		// HSCAN với batch size 1000 - Redis tự động stream từng batch
		await foreach (var entry in db.HashScanAsync(hashKey, pattern: "*", pageSize: 1000))
		{
			try
			{
				var key = JsonSerializer.Deserialize<TKey>(entry.Name.ToString());
				if (key != null)
				{
					keyAction(key);
				}
			}
			catch
			{
				// Skip invalid entries
			}
		}
	}

	/// <summary>
	/// Stream all values với HSCAN - tối ưu memory cho map có hàng triệu phần tử
	/// Không load toàn bộ values vào memory cùng lúc
	/// </summary>
	public async Task GetAllValuesAsync(Action<TValue> valueAction)
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		
		// HSCAN với batch size 1000 - Redis tự động stream từng batch
		await foreach (var entry in db.HashScanAsync(hashKey, pattern: "*", pageSize: 1000))
		{
			try
			{
				var value = DeserializeValue(entry.Value!);
				if (value != null)
				{
					valueAction(value);
				}
			}
			catch
			{
				// Skip invalid entries
			}
		}
	}

	/// <summary>
	/// Stream all entries với HSCAN - tối ưu memory cho map có hàng triệu phần tử
	/// Không load toàn bộ entries vào memory cùng lúc
	/// </summary>
	public async Task GetAllEntriesAsync(Action<IEntry<TKey, TValue>> entryAction)
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		
		// HSCAN với batch size 1000 - Redis tự động stream từng batch
		await foreach (var entry in db.HashScanAsync(hashKey, pattern: "*", pageSize: 1000))
		{
			try
			{
				var key = JsonSerializer.Deserialize<TKey>(entry.Name.ToString());
				var value = DeserializeValue(entry.Value!);
				
				if (key != null && value != null)
				{
					entryAction(new Entry<TKey, TValue>(key, value));
				}
			}
			catch
			{
				// Skip invalid entries
			}
		}
	}

	public async Task<bool> RemoveAsync(TKey key)
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		var fieldName = SerializeKey(key);
		
		// Lấy value trước khi xóa để trigger callback
		var existingValue = await db.HashGetAsync(hashKey, fieldName);
		if (!existingValue.HasValue)
		{
			return false;
		}
		
		var deleted = await db.HashDeleteAsync(hashKey, fieldName);
		
		if (deleted)
		{
			// Remove version metadata from Redis
			await RemoveVersionFromRedisAsync(key);
			
			// Xóa khỏi access time tracking nếu có TTL
			var ttl = await GetItemTtlFromRedisAsync();
			if (ttl.HasValue)
			{
				var accessTimeKey = GetAccessTimeKey();
				await db.SortedSetRemoveAsync(accessTimeKey, fieldName);
			}
			
			// Trigger callback
			try
			{
				var value = DeserializeValue(existingValue!);
				if (value != null)
				{
					TriggerRemoveHandlers(key, value);
				}
			}
			catch
			{
				// Skip if deserialization fails
			}
		}
		
		return deleted;
	}

	#endregion

	private void ProcessBatch(object? state)
	{
		// Skip if no batch handlers registered
		if (_onBatchUpdateHandlers.Count == 0)
		{
			return;
		}

		// Timer callback cannot be async, so fire and forget
		_ = ProcessBatchAsync();
	}

	/// <summary>
	/// NEW OPTIMIZED: Process batch using Sorted Set (100x faster for millions of records)
	/// Uses ZRANGEBYSCORE to query only items in timestamp range
	/// Memory: O(batch_size) instead of O(total_items)
	/// Network: Only fetches relevant items
	/// </summary>
	private async Task ProcessBatchAsync()
	{
		try
		{
			var now = DateTime.UtcNow;
			var batch = new List<IEntry<TKey, TValue>>();

			var db = _redis.GetDatabase(_database);
			var sortedSetKey = GetTimestampsSortedSetKey();
			
			// Check if sorted set exists (migration complete)
			var sortedSetExists = await db.KeyExistsAsync(sortedSetKey);
			
			if (sortedSetExists)
			{
				// USE NEW OPTIMIZED PATH: Sorted Set
				await ProcessBatchAsync_Optimized(now, batch, db);
			}
			else
			{
				// FALLBACK: Use legacy Hash method
				await ProcessBatchAsync_Legacy(now, batch, db);
			}

			if (batch.Count > 0)
			{
				// Update last batch timestamp BEFORE triggering handlers
				var lastBatchKey = $"{GetTimestampsKey()}:last-batch";
				await db.StringSetAsync(lastBatchKey, now.Ticks);
				
				TriggerBatchUpdateHandlers(batch);
			}
		}
		catch (Exception)
		{
			// Ignore batch processing errors
		}
	}

	/// <summary>
	/// OPTIMIZED: Query Sorted Set for items in timestamp range (100x faster)
	/// </summary>
	private async Task ProcessBatchAsync_Optimized(DateTime now, List<IEntry<TKey, TValue>> batch, IDatabase db)
	{
		var sortedSetKey = GetTimestampsSortedSetKey();
		
		// Get last batch time
		var lastBatchKey = $"{GetTimestampsKey()}:last-batch";
		var lastBatchTime = await db.StringGetAsync(lastBatchKey);
		long lastBatchTicks = DateTime.MinValue.Ticks;
		
		if (lastBatchTime.HasValue && long.TryParse(lastBatchTime!, out var ticks))
		{
			lastBatchTicks = ticks;
		}
		
		// Calculate query range
		var minScore = lastBatchTicks; // Items updated after last batch
		var maxScore = now.Add(-_batchWaitTime).Ticks; // Items old enough to batch
		
		// Query Sorted Set: Only items in range (O(log n + k) where k = result size)
		var results = await db.SortedSetRangeByScoreAsync(
			sortedSetKey, 
			start: minScore,
			stop: maxScore,
			exclude: Exclude.Start, // Exclude lastBatchTicks (already processed)
			order: Order.Ascending,
			skip: 0,
			take: -1 // Get all in range (can add limit if needed)
		);
		
		// Fetch values for matched keys
		foreach (var serializedKey in results)
		{
			try
			{
				var key = JsonSerializer.Deserialize<TKey>(serializedKey.ToString(), JsonOptions);
				if (key != null)
				{
					var value = await GetValueAsync(key);
					batch.Add(new Entry<TKey, TValue>(key, value));
				}
			}
			catch (KeyNotFoundException)
			{
				// Key was deleted, skip it
				continue;
			}
			catch
			{
				// Skip invalid keys
				continue;
			}
		}
	}

	/// <summary>
	/// LEGACY: Uses Hash for backward compatibility (slow for large datasets)
	/// </summary>
	private async Task ProcessBatchAsync_Legacy(DateTime now, List<IEntry<TKey, TValue>> batch, IDatabase db)
	{
		// Load all timestamps from Redis Hash (slow)
		var timestamps = await GetAllTimestampsFromRedisAsync();
		
		// Load last batch processed timestamps
		var lastBatchKey = $"{GetTimestampsKey()}:last-batch";
		var lastBatchTime = await db.StringGetAsync(lastBatchKey);
		DateTime lastBatchProcessed = DateTime.MinValue;
		
		if (lastBatchTime.HasValue && long.TryParse(lastBatchTime!, out var ticks))
		{
			lastBatchProcessed = new DateTime(ticks, DateTimeKind.Utc);
		}
		
		foreach (var kvp in timestamps)
		{
			// Check if item was updated AFTER last batch AND enough time has passed
			if (kvp.Value > lastBatchProcessed && now - kvp.Value >= _batchWaitTime)
			{
				try
				{
					var value = await GetValueAsync(kvp.Key);
					batch.Add(new Entry<TKey, TValue>(kvp.Key, value));
				}
				catch (KeyNotFoundException)
				{
					// Key was deleted, skip it
					continue;
				}
			}
		}
	}

	/// <summary>
	/// Get all timestamps from Redis for batch processing
	/// </summary>
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
				var key = JsonSerializer.Deserialize<TKey>(entry.Name.ToString(), JsonOptions);
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

	private void TriggerAddHandlers(TKey key, TValue value)
	{
		lock (_lockObj)
		{
			foreach (var handler in _onAddHandlers)
			{
				try
				{
					handler(key, value);
				}
				catch
				{
					// Ignore handler exceptions
				}
			}
		}
	}

	private void TriggerUpdateHandlers(TKey key, TValue value)
	{
		lock (_lockObj)
		{
			foreach (var handler in _onUpdateHandlers)
			{
				try
				{
					handler(key, value);
				}
				catch
				{
					// Ignore handler exceptions
				}
			}
		}
	}

	private void TriggerRemoveHandlers(TKey key, TValue value)
	{
		lock (_lockObj)
		{
			foreach (var handler in _onRemoveHandlers)
			{
				try
				{
					handler(key, value);
				}
				catch
				{
					// Ignore handler exceptions
				}
			}
		}
	}

	private void TriggerClearHandlers()
	{
		lock (_lockObj)
		{
			foreach (var handler in _onClearHandlers)
			{
				try
				{
					handler();
				}
				catch
				{
					// Ignore handler exceptions
				}
			}
		}
	}

	private void TriggerBatchUpdateHandlers(IEnumerable<IEntry<TKey, TValue>> entries)
	{
		lock (_lockObj)
		{
			foreach (var handler in _onBatchUpdateHandlers)
			{
				try
				{
					handler(entries);
				}
				catch
				{
					// Ignore handler exceptions
				}
			}
		}
	}

	private string GetHashKey() => $"map:{_mapName}";

	private string GetAccessTimeKey() => $"map:{_mapName}:access-time";

	private string SerializeKey(TKey key) => JsonSerializer.Serialize(key, JsonOptions);

	private string SerializeValue(TValue value) => JsonSerializer.Serialize(value, JsonOptions);

	private TValue DeserializeValue(string json) => 
		JsonSerializer.Deserialize<TValue>(json, JsonOptions) ?? throw new InvalidOperationException("Failed to deserialize value");

	/// <summary>
	/// Update access time của key trong sorted set
	/// Score = Unix timestamp (seconds)
	/// </summary>
	private async Task UpdateAccessTimeAsync(TKey key)
	{
		var db = _redis.GetDatabase(_database);
		var accessTimeKey = GetAccessTimeKey();
		var fieldName = SerializeKey(key);
		var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		
		await db.SortedSetAddAsync(accessTimeKey, fieldName, now);
	}

	/// <summary>
	/// Background task để xóa các keys đã hết hạn
	/// Chạy mỗi giây
	/// </summary>
	private void ProcessExpiration(object? state)
	{
		// Timer callback cannot be async, so fire and forget
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

			// Lấy tất cả keys có access time < threshold (đã hết hạn)
			// ZRANGEBYSCORE key -inf threshold
			var expiredKeys = await db.SortedSetRangeByScoreAsync(
				accessTimeKey,
				double.NegativeInfinity,
				expirationThreshold
			);

			if (expiredKeys.Length == 0)
			{
				return;
			}

			// Xóa từng key đã hết hạn
			foreach (var expiredKeyValue in expiredKeys)
			{
				try
				{
					var serializedKey = expiredKeyValue.ToString();
					
					// Get value trước khi xóa để trigger callback
					var value = await db.HashGetAsync(hashKey, serializedKey);
					
					if (value.HasValue)
					{
						// Xóa khỏi hash
						await db.HashDeleteAsync(hashKey, serializedKey);
						
						// Xóa khỏi sorted set
						await db.SortedSetRemoveAsync(accessTimeKey, serializedKey);
						
						// Deserialize key và value để trigger callback
						var key = JsonSerializer.Deserialize<TKey>(serializedKey, JsonOptions);
						if (key != null)
						{
							var deserializedValue = DeserializeValue(value!);
							
							// Remove version metadata from Redis
							await RemoveVersionFromRedisAsync(key);
							
							// Trigger expired handlers
							TriggerExpiredHandlers(key, deserializedValue);
							TriggerRemoveHandlers(key, deserializedValue);
						}
					}
					else
					{
						// Key không tồn tại trong hash, xóa khỏi sorted set
						await db.SortedSetRemoveAsync(accessTimeKey, serializedKey);
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

	private void TriggerExpiredHandlers(TKey key, TValue value)
	{
		lock (_lockObj)
		{
			foreach (var handler in _onExpiredHandlers)
			{
				try
				{
					handler(key, value);
				}
				catch
				{
					// Ignore handler exceptions
				}
			}
		}
	}

	// ==================== REDIS METADATA HELPER METHODS ====================
	// All metadata now stored in Redis for multi-instance synchronization

	/// <summary>
	/// Redis Keys Structure:
	/// - map:{mapName}:__meta:versions    → Hash: Version tracking (Guid)
	/// - map:{mapName}:__meta:timestamps  → Hash: Last modified timestamps (OLD - deprecated)
	/// - map:{mapName}:__meta:timestamps-sorted → Sorted Set: Timestamps indexed by time (NEW - optimized)
	/// - map:{mapName}:__meta:ttl-config  → String: TTL configuration (seconds)
	/// Note: Access time already uses map:{mapName}:access-time (no __meta prefix for backward compatibility)
	/// </summary>

	private string GetVersionsKey() => $"map:{_mapName}:__meta:versions";

	private string GetTimestampsKey() => $"map:{_mapName}:__meta:timestamps";

	private string GetTimestampsSortedSetKey() => $"map:{_mapName}:__meta:timestamps-sorted";

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
	/// DUAL WRITE: Writes to both Hash (legacy) and Sorted Set (new optimized structure)
	/// </summary>
	private async Task SetTimestampInRedisAsync(TKey key, DateTime timestamp)
	{
		var db = _redis.GetDatabase(_database);
		var fieldName = SerializeKey(key);
		
		// Write to Hash (legacy - for backward compatibility)
		var timestampsKey = GetTimestampsKey();
		await db.HashSetAsync(timestampsKey, fieldName, timestamp.Ticks);
		
		// Write to Sorted Set (new - optimized for range queries)
		var sortedSetKey = GetTimestampsSortedSetKey();
		var score = timestamp.Ticks; // Score = timestamp (sortable)
		await db.SortedSetAddAsync(sortedSetKey, fieldName, score);
	}

	/// <summary>
	/// Format timestamp to human-readable "time ago" format
	/// </summary>
	private string FormatTimeAgo(DateTime timestamp)
	{
		var now = DateTime.UtcNow;
		var diff = now - timestamp;
		
		if (diff.TotalSeconds < 60)
			return $"{(int)diff.TotalSeconds}s ago";
		
		if (diff.TotalMinutes < 60)
			return $"{(int)diff.TotalMinutes}m ago";
		
		if (diff.TotalHours < 24)
			return $"{(int)diff.TotalHours}h ago";
		
		if (diff.TotalDays < 30)
			return $"{(int)diff.TotalDays}d ago";
		
		if (diff.TotalDays < 365)
			return $"{(int)(diff.TotalDays / 30)}mo ago";
		
		return $"{(int)(diff.TotalDays / 365)}y ago";
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
		
		// Remove from timestamps hash (legacy)
		await db.HashDeleteAsync(GetTimestampsKey(), fieldName);
		
		// Remove from timestamps sorted set (new)
		await db.SortedSetRemoveAsync(GetTimestampsSortedSetKey(), fieldName);
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
		await db.KeyDeleteAsync(GetTimestampsSortedSetKey());
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

	// ==================== MIGRATION HELPER METHODS ====================

	/// <summary>
	/// Migrate timestamps from Hash to Sorted Set (for performance optimization)
	/// Call this once after deploying the new code
	/// </summary>
	public async Task MigrateTimestampsToSortedSetAsync()
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetTimestampsKey();
		var sortedSetKey = GetTimestampsSortedSetKey();
		
		// Check if already migrated
		var sortedSetExists = await db.KeyExistsAsync(sortedSetKey);
		if (sortedSetExists)
		{
			var count = await db.SortedSetLengthAsync(sortedSetKey);
			Console.WriteLine($"[MIGRATION] Sorted Set already exists with {count} entries. Skipping migration.");
			return;
		}
		
		Console.WriteLine($"[MIGRATION] Starting migration for map: {_mapName}");
		
		// Read all timestamps from Hash
		var hashEntries = await db.HashGetAllAsync(hashKey);
		Console.WriteLine($"[MIGRATION] Found {hashEntries.Length} timestamps in Hash");
		
		if (hashEntries.Length == 0)
		{
			Console.WriteLine($"[MIGRATION] No data to migrate for map: {_mapName}");
			return;
		}
		
		// Prepare batch for Sorted Set
		var sortedSetEntries = new List<SortedSetEntry>();
		int validCount = 0;
		int invalidCount = 0;
		
		foreach (var entry in hashEntries)
		{
			try
			{
				var serializedKey = entry.Name.ToString();
				var timestampTicks = (long)entry.Value;
				
				// Add to sorted set (score = timestamp ticks)
				sortedSetEntries.Add(new SortedSetEntry(serializedKey, timestampTicks));
				validCount++;
			}
			catch
			{
				invalidCount++;
				continue;
			}
		}
		
		// Write to Sorted Set in one batch
		if (sortedSetEntries.Count > 0)
		{
			await db.SortedSetAddAsync(sortedSetKey, sortedSetEntries.ToArray());
			Console.WriteLine($"[MIGRATION] Migrated {validCount} entries to Sorted Set");
		}
		
		if (invalidCount > 0)
		{
			Console.WriteLine($"[MIGRATION] Skipped {invalidCount} invalid entries");
		}
		
		// Verify migration
		var sortedSetCount = await db.SortedSetLengthAsync(sortedSetKey);
		Console.WriteLine($"[MIGRATION] Verification: Sorted Set now has {sortedSetCount} entries");
		Console.WriteLine($"[MIGRATION] Migration complete for map: {_mapName}");
	}

	/// <summary>
	/// Get migration status (for monitoring)
	/// </summary>
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

internal sealed class Entry<TKey, TValue> : IEntry<TKey, TValue>
{
	private readonly TKey _key;
	private readonly TValue _value;

	public Entry(TKey key, TValue value)
	{
		_key = key;
		_value = value;
	}

	public TKey GetKey() => _key;
	public TValue GetValue() => _value;
}

public class MapEntryData
{
	public string Key { get; set; } = string.Empty;
	public string Value { get; set; } = string.Empty;
	public string Version { get; set; } = string.Empty;
	public string LastModified { get; set; } = string.Empty;
	public long LastModifiedTicks { get; set; }
}

public class PagedMapEntries
{
	public List<MapEntryData> Entries { get; set; } = new();
	public int CurrentPage { get; set; }
	public int PageSize { get; set; }
	public int TotalCount { get; set; }
	public int TotalPages { get; set; }
	public bool HasNext { get; set; }
	public bool HasPrev { get; set; }
}

public class MigrationStatus
{
	public string MapName { get; set; } = string.Empty;
	public long HashCount { get; set; }
	public long SortedSetCount { get; set; }
	public bool IsMigrated { get; set; }
	public bool IsComplete { get; set; }
}
