using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace CacheManager.Core;

internal sealed partial class RedisMap<TKey, TValue> : IMap<TKey, TValue>
	where TKey : notnull
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		WriteIndented = false
	};

	private readonly IConnectionMultiplexer _redis;
	private readonly string _mapName;
	private readonly int _database;
	private readonly TimeSpan _batchWaitTime;
	private readonly List<Func<TKey, TValue, Task>> _onAddHandlers = new();
	private readonly List<Func<TKey, TValue, Task>> _onUpdateHandlers = new();
	private readonly List<Func<TKey, TValue, Task>> _onRemoveHandlers = new();
	private readonly List<Func<Task>> _onClearHandlers = new();
	private readonly List<Func<IEnumerable<IEntry<TKey, TValue>>, Task>> _onBatchUpdateHandlers = new();
	private readonly List<Func<TKey, TValue, Task>> _onExpiredHandlers = new();
	private readonly SemaphoreSlim _lockObj = new(1, 1);
	private readonly IRedisValueFormatter<TValue> _valueFormatter;

	private Timer? _batchTimer;
	private Timer? _expirationTimer;
	private TimeSpan? _itemTtl;

	public RedisMap(
		IConnectionMultiplexer redis,
		string mapName,
		int database,
		TimeSpan batchWaitTime,
		IRedisValueFormatter<TValue>? valueFormatter = null)
	{
		_redis = redis ?? throw new ArgumentNullException(nameof(redis));
		_mapName = mapName ?? throw new ArgumentNullException(nameof(mapName));
		_database = database;
		_batchWaitTime = batchWaitTime;
		_valueFormatter = valueFormatter ?? new JsonRedisValueFormatter<TValue>(JsonOptions);

		_batchTimer = new Timer(ProcessBatch, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
		_ = InitializeTtlFromRedisAsync();
	}

	public async Task<TValue?> GetValueAsync(TKey key)
	{
		if (key == null)
		{
			throw new ArgumentNullException(nameof(key));
		}

		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		var serializedKey = SerializeKey(key);
		var (found, value) = await TryReadValueWithLeaseAsync(db, hashKey, serializedKey);
		if (!found)
		{
			return default;
		}

		var ttl = await GetItemTtlFromRedisAsync();
		if (ttl.HasValue)
		{
			await UpdateAccessTimeAsync(key);
		}

		return value;
	}

	public async Task SetValueAsync(TKey key, TValue value)
	{
		if (key == null)
		{
			throw new ArgumentNullException(nameof(key));
		}

		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		var fieldName = SerializeKey(key);
		using var serializedValue = SerializeValue(value);

		var existed = await db.HashExistsAsync(hashKey, fieldName);
		await db.HashSetAsync(hashKey, fieldName, serializedValue.Value);

		var ttl = await GetItemTtlFromRedisAsync();
		if (ttl.HasValue)
		{
			await UpdateAccessTimeAsync(key);
		}

		var newVersion = Guid.NewGuid();
		var timestamp = DateTime.UtcNow;
		await SetVersionInRedisAsync(key, newVersion);
		await SetTimestampInRedisAsync(key, timestamp);

		if (existed)
		{
			await TriggerUpdateHandlers(key, value);
		}
		else
		{
			await TriggerAddHandlers(key, value);
		}
	}

	public void OnAdd(Func<TKey, TValue, Task> addAction)
	{
		if (addAction == null)
		{
			throw new ArgumentNullException(nameof(addAction));
		}

		lock (_lockObj)
		{
			_onAddHandlers.Add(addAction);
		}
	}

	public void OnUpdate(Func<TKey, TValue, Task> updateAction)
	{
		if (updateAction == null)
		{
			throw new ArgumentNullException(nameof(updateAction));
		}

		lock (_lockObj)
		{
			_onUpdateHandlers.Add(updateAction);
		}
	}

	public void OnRemove(Func<TKey, TValue, Task> removeAction)
	{
		if (removeAction == null)
		{
			throw new ArgumentNullException(nameof(removeAction));
		}

		lock (_lockObj)
		{
			_onRemoveHandlers.Add(removeAction);
		}
	}

	public void OnClear(Func<Task> clearAction)
	{
		if (clearAction == null)
		{
			throw new ArgumentNullException(nameof(clearAction));
		}

		lock (_lockObj)
		{
			_onClearHandlers.Add(clearAction);
		}
	}

	public void OnBatchUpdate(Func<IEnumerable<IEntry<TKey, TValue>>, Task> batchUpdateAction)
	{
		if (batchUpdateAction == null)
		{
			throw new ArgumentNullException(nameof(batchUpdateAction));
		}

		lock (_lockObj)
		{
			_onBatchUpdateHandlers.Add(batchUpdateAction);
		}
	}

	public void OnExpired(Func<TKey, TValue, Task> expiredAction)
	{
		if (expiredAction == null)
		{
			throw new ArgumentNullException(nameof(expiredAction));
		}

		lock (_lockObj)
		{
			_onExpiredHandlers.Add(expiredAction);
		}
	}

	public void SetItemExpiration(TimeSpan? ttl)
	{
		_itemTtl = ttl;
		_ = SetItemTtlInRedisAsync(ttl);

		lock (_lockObj)
		{
			if (ttl.HasValue)
			{
				EnsureExpirationTimerLocked();
			}
			else
			{
				StopExpirationTimerLocked();
			}
		}
	}

	public async Task<bool> ContainsKeyAsync(TKey key)
	{
		if (key == null)
		{
			throw new ArgumentNullException(nameof(key));
		}

		var db = _redis.GetDatabase(_database);
		return await db.HashExistsAsync(GetHashKey(), SerializeKey(key));
	}

	public async Task<int> CountAsync()
	{
		var db = _redis.GetDatabase(_database);
		return (int)await db.HashLengthAsync(GetHashKey());
	}

	public async Task<IEnumerable<TValue>> GetAllValuesAsync()
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		var serializedKeys = await db.HashKeysAsync(hashKey);
		var result = new List<TValue>(serializedKeys.Length);

		foreach (var serializedKey in serializedKeys)
		{
			var (found, value) = await TryReadValueWithLeaseAsync(db, hashKey, serializedKey);
			if (!found)
			{
				continue;
			}

			result.Add(value!);
		}

		return result;
	}

	public async Task<IEnumerable<TKey>> GetAllKeysAsync()
	{
		var db = _redis.GetDatabase(_database);
		var keys = await db.HashKeysAsync(GetHashKey());
		var result = new List<TKey>(keys.Length);

		foreach (var rawKey in keys)
		{
			if (TryDeserializeKey(rawKey, out var key, out _) && key != null)
			{
				result.Add(key);
			}
		}

		return result;
	}

	public async Task<IEnumerable<IEntry<TKey, TValue>>> GetAllEntriesAsync()
	{
		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		var serializedKeys = await db.HashKeysAsync(hashKey);
		var result = new List<IEntry<TKey, TValue>>(serializedKeys.Length);

		foreach (var serializedKey in serializedKeys)
		{
			if (!TryDeserializeKey(serializedKey, out var key, out _) || key == null)
			{
				continue;
			}

			var (found, value) = await TryReadValueWithLeaseAsync(db, hashKey, serializedKey);
			if (!found)
			{
				continue;
			}

			result.Add(new Entry<TKey, TValue>(key, value!));
		}

		return result;
	}

	public async Task GetAllKeysAsync(Action<TKey> keyAction)
	{
		if (keyAction == null)
		{
			throw new ArgumentNullException(nameof(keyAction));
		}

		var db = _redis.GetDatabase(_database);
		await foreach (var entry in db.HashScanAsync(GetHashKey()))
		{
			if (TryDeserializeKey(entry.Name, out var key, out _) && key != null)
			{
				keyAction(key);
			}
		}
	}

	public async Task GetAllValuesAsync(Action<TValue> valueAction)
	{
		if (valueAction == null)
		{
			throw new ArgumentNullException(nameof(valueAction));
		}

		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		await foreach (var entry in db.HashScanAsync(hashKey))
		{
			var (found, value) = await TryReadValueWithLeaseAsync(db, hashKey, entry.Name);
			if (!found)
			{
				continue;
			}

			valueAction(value!);
		}
	}

	public async Task GetAllEntriesAsync(Action<IEntry<TKey, TValue>> entryAction)
	{
		if (entryAction == null)
		{
			throw new ArgumentNullException(nameof(entryAction));
		}

		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		await foreach (var entry in db.HashScanAsync(hashKey))
		{
			if (!TryDeserializeKey(entry.Name, out var key, out _) || key == null)
			{
				continue;
			}

			var (found, value) = await TryReadValueWithLeaseAsync(db, hashKey, entry.Name);
			if (!found)
			{
				continue;
			}

			entryAction(new Entry<TKey, TValue>(key, value!));
		}
	}

	public async Task<bool> RemoveAsync(TKey key)
	{
		if (key == null)
		{
			throw new ArgumentNullException(nameof(key));
		}

		var db = _redis.GetDatabase(_database);
		var hashKey = GetHashKey();
		var serializedKey = SerializeKey(key);
		var (found, removedValue) = await TryReadValueWithLeaseAsync(db, hashKey, serializedKey);
		if (!found)
		{
			return false;
		}

		if (!await db.HashDeleteAsync(hashKey, serializedKey))
		{
			if (removedValue != null)
			{
				ReturnValueToPoolIfNeeded(removedValue);
			}

			return false;
		}

		await db.SortedSetRemoveAsync(GetAccessTimeKey(), serializedKey);
		await RemoveVersionFromRedisAsync(key);

		await TriggerRemoveHandlers(key, removedValue!);

		if (removedValue != null)
		{
			ReturnValueToPoolIfNeeded(removedValue);
		}

		return true;
	}

	public async Task ClearAsync()
	{
		var db = _redis.GetDatabase(_database);
		await db.KeyDeleteAsync(GetHashKey());
		await db.KeyDeleteAsync(GetAccessTimeKey());
		await ClearAllVersionMetadataAsync();
		await TriggerClearHandlers();
	}

	private async Task<(bool Found, TValue? Value)> TryReadValueWithLeaseAsync(IDatabase db, RedisKey hashKey, RedisValue serializedKey)
	{
		var lease = await db.HashGetLeaseAsync(hashKey, serializedKey);

		if (lease == null)
		{
			return (false, default);
		}

		using (lease)
		{
			if (lease.Length == 0)
			{
				var exists = await db.HashExistsAsync(hashKey, serializedKey);
				if (!exists)
				{
					return (false, default);
				}
			}

			return (true, DeserializeValue(lease.Span));
		}
	}

	private async Task InitializeTtlFromRedisAsync()
	{
		try
		{
			var ttl = await GetItemTtlFromRedisAsync();
			if (!ttl.HasValue)
			{
				return;
			}

			lock (_lockObj)
			{
				_itemTtl = ttl;
				EnsureExpirationTimerLocked();
			}
		}
		catch
		{
			// Ignore initialization errors to keep map usable.
		}
	}

	private void EnsureExpirationTimerLocked()
	{
		_expirationTimer ??= new Timer(ProcessExpiration, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
	}

	private void StopExpirationTimerLocked()
	{
		_expirationTimer?.Dispose();
		_expirationTimer = null;
	}
}

