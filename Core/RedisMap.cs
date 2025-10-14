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
	private readonly ConcurrentDictionary<TKey, MapEntry> _versionCache;
	private readonly List<Action<TKey, TValue>> _onAddHandlers;
	private readonly List<Action<TKey, TValue>> _onUpdateHandlers;
	private readonly List<Action<TKey, TValue>> _onRemoveHandlers;
	private readonly List<Action> _onClearHandlers;
	private readonly List<Action<IEnumerable<IEntry<TKey, TValue>>>> _onBatchUpdateHandlers;
	private readonly Timer? _batchTimer;
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
		_versionCache = new ConcurrentDictionary<TKey, MapEntry>();
		_onAddHandlers = new List<Action<TKey, TValue>>();
		_onUpdateHandlers = new List<Action<TKey, TValue>>();
		_onRemoveHandlers = new List<Action<TKey, TValue>>();
		_onClearHandlers = new List<Action>();
		_onBatchUpdateHandlers = new List<Action<IEnumerable<IEntry<TKey, TValue>>>>();
		_batchWaitTime = batchWaitTime ?? TimeSpan.FromSeconds(5);

		if (_onBatchUpdateHandlers.Count > 0)
		{
			_batchTimer = new Timer(ProcessBatch, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
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

		// Update version cache
		var entry = new MapEntry
		{
			Key = key,
			Value = value,
			Version = Guid.NewGuid(),
			LastUpdated = DateTime.UtcNow
		};
		_versionCache.AddOrUpdate(key, entry, (_, _) => entry);

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

	public async Task ClearAsync()
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		await db.KeyDeleteAsync(hashKey);
		_versionCache.Clear();
		TriggerClearHandlers();
	}

	public async Task<IEnumerable<MapEntryData>> GetAllEntriesAsync()
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
					var version = _versionCache.TryGetValue(key, out var cached) 
						? cached.Version.ToString() 
						: Guid.NewGuid().ToString();
					
					result.Add(new MapEntryData
					{
						Key = key.ToString() ?? "",
						Value = value?.ToString() ?? "",
						Version = version
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
					var version = _versionCache.TryGetValue(key, out var cached) 
						? cached.Version.ToString() 
						: Guid.NewGuid().ToString();
					
					result.Add(new MapEntryData
					{
						Key = key.ToString() ?? "",
						Value = value?.ToString() ?? "",
						Version = version
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
					var version = _versionCache.TryGetValue(key, out var cached) 
						? cached.Version.ToString() 
						: Guid.NewGuid().ToString();
					
					matchedEntries.Add(new MapEntryData
					{
						Key = keyString,
						Value = value?.ToString() ?? "",
						Version = version
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

	private void ProcessBatch(object? state)
	{
		var now = DateTime.UtcNow;
		var batch = new List<IEntry<TKey, TValue>>();

		foreach (var kvp in _versionCache)
		{
			if (now - kvp.Value.LastUpdated >= _batchWaitTime)
			{
				batch.Add(new Entry<TKey, TValue>(kvp.Key, kvp.Value.Value));
				_versionCache.TryRemove(kvp.Key, out _);
			}
		}

		if (batch.Count > 0)
		{
			TriggerBatchUpdateHandlers(batch);
		}
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

	private string SerializeKey(TKey key) => JsonSerializer.Serialize(key, JsonOptions);

	private string SerializeValue(TValue value) => JsonSerializer.Serialize(value, JsonOptions);

	private TValue DeserializeValue(string json) => 
		JsonSerializer.Deserialize<TValue>(json, JsonOptions) ?? throw new InvalidOperationException("Failed to deserialize value");

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
