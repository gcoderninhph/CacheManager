using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace CacheManager.Core;

internal sealed class RedisBucket<TValue>
{
	private readonly IConnectionMultiplexer _redis;
	private readonly string _bucketName;
	private readonly int _database;
	
	// JSON serialization options - mặc định format đẹp, camelCase
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = false, // Compact để tiết kiệm bandwidth
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // camelCase cho properties
		PropertyNameCaseInsensitive = true, // Case-insensitive khi deserialize
		DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
	};

	public RedisBucket(IConnectionMultiplexer redis, string bucketName, int database = -1)
	{
		_redis = redis;
		_bucketName = bucketName;
		_database = database;
	}

	public async Task AddAsync(TValue value)
	{
		var db = _redis.GetDatabase(_database);
		var listKey = GetListKey();
		var serializedValue = JsonSerializer.Serialize(value, JsonOptions);
		await db.ListRightPushAsync(listKey, serializedValue);
	}

	public async Task<TValue?> PopAsync()
	{
		var db = _redis.GetDatabase(_database);
		var listKey = GetListKey();
		var value = await db.ListLeftPopAsync(listKey);
		
		if (!value.HasValue)
		{
			return default;
		}

		return JsonSerializer.Deserialize<TValue>(value!, JsonOptions);
	}

	public async Task<long> CountAsync()
	{
		var db = _redis.GetDatabase(_database);
		var listKey = GetListKey();
		return await db.ListLengthAsync(listKey);
	}

	public async Task<IEnumerable<TValue>> GetAllAsync()
	{
		var db = _redis.GetDatabase(_database);
		var listKey = GetListKey();
		var values = await db.ListRangeAsync(listKey);
		return values.Select(v => JsonSerializer.Deserialize<TValue>(v.ToString(), JsonOptions)!);
	}

	public async Task ClearAsync()
	{
		var db = _redis.GetDatabase(_database);
		var listKey = GetListKey();
		await db.KeyDeleteAsync(listKey);
	}

	private string GetListKey() => $"bucket:{_bucketName}";
}
